using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class TemplateService : IAionTemplateMarketplaceService, ITemplateService
{
    private static readonly JsonSerializerOptions PackageSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AionDbContext _db;
    private readonly string _marketplaceFolder;
    private readonly ISecurityAuditService _securityAudit;
    private readonly ICurrentUserService _currentUserService;
    private readonly IModuleApplier _moduleApplier;
    private readonly ILifeService _timeline;

    public TemplateService(
        AionDbContext db,
        IOptions<MarketplaceOptions> options,
        ISecurityAuditService securityAudit,
        ICurrentUserService currentUserService,
        IModuleApplier moduleApplier,
        ILifeService timeline)
    {
        _db = db;
        ArgumentNullException.ThrowIfNull(options);
        _marketplaceFolder = options.Value.MarketplaceFolder ?? throw new InvalidOperationException("Marketplace folder is required");
        _securityAudit = securityAudit;
        _currentUserService = currentUserService;
        _moduleApplier = moduleApplier;
        _timeline = timeline;
        Directory.CreateDirectory(_marketplaceFolder);
    }

    public async Task<TemplatePackage> ExportModuleAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        var module = await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .Include(m => m.Reports)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Actions)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Conditions)
            .FirstAsync(m => m.Id == moduleId, cancellationToken)
            .ConfigureAwait(false);

        var entityIds = module.EntityTypes.Select(e => e.Id).ToList();
        var tables = await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .Where(t => entityIds.Contains(t.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var moduleSpec = BuildModuleSpec(module, tables);
        var payload = JsonSerializer.Serialize(moduleSpec, PackageSerializerOptions);
        var assetsManifest = BuildAssetsManifest();
        var assetsManifestJson = JsonSerializer.Serialize(assetsManifest, PackageSerializerOptions);
        var author = ResolveAuthor();
        var version = module.Version.ToString(CultureInfo.InvariantCulture);
        var signature = ComputeSignature(author, version, payload, assetsManifestJson);

        var package = new TemplatePackage
        {
            Name = module.Name,
            Description = module.Description,
            Payload = payload,
            Version = version,
            Author = author,
            Signature = signature,
            AssetsManifest = assetsManifestJson
        };

        await _db.Templates.AddAsync(package, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.ModuleExport,
            "module.exported",
            "module",
            module.Id,
            new Dictionary<string, object?>
            {
                ["moduleName"] = module.Name,
                ["templateId"] = package.Id,
                ["version"] = package.Version
            }), cancellationToken).ConfigureAwait(false);
        await _timeline.AddHistoryAsync(new S_HistoryEvent
        {
            Title = "Module exporté",
            Description = module.Name,
            OccurredAt = DateTimeOffset.UtcNow,
            ModuleId = module.Id
        }, cancellationToken).ConfigureAwait(false);
        return package;
    }

    public async Task<S_Module> ImportModuleAsync(TemplatePackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        NormalizePackage(package);
        ValidateOrUpdateSignature(package);

        var payload = ParseTemplatePayload(package.Payload, package.Name);
        var module = payload.Module ?? BuildModuleFromSpec(payload.ModuleSpec, package.Name);
        EnsureModuleMetadata(module);

        var existing = await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .FirstOrDefaultAsync(m => m.Id == module.Id || m.Name == module.Name, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await _db.Modules.AddAsync(module, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            module = existing;
            UpsertModuleFromSpec(module, payload.ModuleSpec);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _moduleApplier.ApplyAsync(payload.ModuleSpec, cancellationToken: cancellationToken).ConfigureAwait(false);
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        EnsureModuleMetadata(module);
        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.ModuleImport,
            "module.imported",
            "module",
            module.Id,
            new Dictionary<string, object?>
            {
                ["moduleName"] = module.Name,
                ["templateId"] = package.Id,
                ["version"] = package.Version
            }), cancellationToken).ConfigureAwait(false);
        await _timeline.AddHistoryAsync(new S_HistoryEvent
        {
            Title = "Module importé",
            Description = module.Name,
            OccurredAt = DateTimeOffset.UtcNow,
            ModuleId = module.Id
        }, cancellationToken).ConfigureAwait(false);
        return module;
    }

    private async Task CreateOrUpdateMarketplaceEntry(TemplatePackage package, CancellationToken cancellationToken)
    {
        var fileName = Path.Combine(_marketplaceFolder, $"{package.Id}.json");
        var packageJson = JsonSerializer.Serialize(package, PackageSerializerOptions);
        await File.WriteAllTextAsync(fileName, packageJson, cancellationToken).ConfigureAwait(false);

        if (!await _db.Marketplace.AnyAsync(i => i.Id == package.Id, cancellationToken).ConfigureAwait(false))
        {
            await _db.Marketplace.AddAsync(new MarketplaceItem
            {
                Id = package.Id,
                Name = package.Name,
                Category = "Module",
                PackagePath = fileName
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var existing = await _db.Marketplace.FirstAsync(i => i.Id == package.Id, cancellationToken).ConfigureAwait(false);
            existing.PackagePath = fileName;
            existing.Name = package.Name;
            _db.Marketplace.Update(existing);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureModuleMetadata(S_Module module)
    {
        var now = DateTimeOffset.UtcNow;
        if (module.ModifiedAt == default)
        {
            module.ModifiedAt = now;
        }

        if (module.Version <= 0)
        {
            module.Version = 1;
        }
    }

    public async Task<IEnumerable<MarketplaceItem>> GetMarketplaceAsync(CancellationToken cancellationToken = default)
    {
        // Synchronise le disque et la base pour rester cohérent avec les fichiers locaux
        foreach (var file in Directory.EnumerateFiles(_marketplaceFolder, "*.json"))
        {
            var package = await TryReadPackageAsync(file, cancellationToken).ConfigureAwait(false);
            var fileName = Path.GetFileNameWithoutExtension(file);
            var id = package?.Id ?? ResolveMarketplaceId(fileName);
            if (!await _db.Marketplace.AnyAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false))
            {
                await _db.Marketplace.AddAsync(new MarketplaceItem
                {
                    Id = id,
                    Name = package?.Name ?? Path.GetFileName(file),
                    Category = "Module",
                    PackagePath = file
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await _db.Marketplace.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ModuleSpec BuildModuleSpec(S_Module module, IReadOnlyCollection<STable> tables)
    {
        var tableById = tables.ToDictionary(t => t.Id);
        var entityById = module.EntityTypes.ToDictionary(e => e.Id);
        var moduleSpec = new ModuleSpec
        {
            ModuleId = module.Id,
            Slug = ResolveModuleSlug(module.Name),
            DisplayName = module.Name,
            Description = module.Description,
            Tables = new List<TableSpec>()
        };

        foreach (var entity in module.EntityTypes)
        {
            tableById.TryGetValue(entity.Id, out var table);
            moduleSpec.Tables.Add(MapTable(entity, table, entityById));
        }

        return moduleSpec;
    }

    private static TableSpec MapTable(S_EntityType entity, STable? table, IReadOnlyDictionary<Guid, S_EntityType> entityById)
    {
        if (table is not null)
        {
            return new TableSpec
            {
                Id = table.Id,
                Slug = table.Name,
                DisplayName = table.DisplayName,
                Description = table.Description,
                IsSystem = table.IsSystem,
                SupportsSoftDelete = table.SupportsSoftDelete,
                HasAuditTrail = table.HasAuditTrail,
                DefaultView = table.DefaultView,
                RowLabelTemplate = table.RowLabelTemplate,
                Fields = table.Fields.Select(MapField).ToList(),
                Views = table.Views.Select(MapView).ToList()
            };
        }

        return new TableSpec
        {
            Id = entity.Id,
            Slug = entity.Name,
            DisplayName = entity.PluralName,
            Description = entity.Description,
            Fields = entity.Fields.Select(field => MapField(field, entityById)).ToList()
        };
    }

    private static FieldSpec MapField(SFieldDefinition field)
    {
        return new FieldSpec
        {
            Id = field.Id,
            Slug = field.Name,
            Label = field.Label,
            DataType = MapDataType(field.DataType),
            IsRequired = field.IsRequired,
            IsSearchable = field.IsSearchable,
            IsListVisible = field.IsListVisible,
            IsPrimaryKey = field.IsPrimaryKey,
            IsUnique = field.IsUnique,
            IsIndexed = field.IsIndexed,
            IsFilterable = field.IsFilterable,
            IsSortable = field.IsSortable,
            IsHidden = field.IsHidden,
            IsReadOnly = field.IsReadOnly,
            IsComputed = field.IsComputed,
            DefaultValue = DeserializeDefault(field.DefaultValue),
            EnumValues = string.IsNullOrWhiteSpace(field.EnumValues) ? null : field.EnumValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Lookup = string.IsNullOrWhiteSpace(field.LookupTarget)
                ? null
                : new LookupSpec
                {
                    TargetTableSlug = field.LookupTarget!,
                    LabelField = field.LookupField
                },
            ComputedExpression = field.ComputedExpression,
            MinLength = field.MinLength,
            MaxLength = field.MaxLength,
            MinValue = field.MinValue,
            MaxValue = field.MaxValue,
            ValidationPattern = field.ValidationPattern,
            Placeholder = field.Placeholder,
            Unit = field.Unit
        };
    }

    private static FieldSpec MapField(S_Field field, IReadOnlyDictionary<Guid, S_EntityType> entityById)
    {
        LookupSpec? lookup = null;
        if (field.DataType == FieldDataType.Lookup && field.RelationTargetEntityTypeId.HasValue &&
            entityById.TryGetValue(field.RelationTargetEntityTypeId.Value, out var targetEntity))
        {
            lookup = new LookupSpec { TargetTableSlug = targetEntity.Name };
        }

        return new FieldSpec
        {
            Id = field.Id,
            Slug = field.Name,
            Label = field.Label,
            DataType = MapDataType(field.DataType),
            IsRequired = field.IsRequired,
            IsSearchable = field.IsSearchable,
            IsListVisible = field.IsListVisible,
            DefaultValue = DeserializeDefault(field.DefaultValue),
            EnumValues = string.IsNullOrWhiteSpace(field.EnumValues)
                ? null
                : field.EnumValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Lookup = lookup
        };
    }

    private static ViewSpec MapView(SViewDefinition view)
    {
        return new ViewSpec
        {
            Id = view.Id,
            Slug = view.Name,
            DisplayName = view.DisplayName,
            Description = view.Description,
            Filter = DeserializeFilter(view.QueryDefinition),
            Sort = view.SortExpression,
            PageSize = view.PageSize,
            Visualization = view.Visualization,
            IsDefault = view.IsDefault
        };
    }

    private static TemplateAssetsManifest BuildAssetsManifest()
        => new();

    private string ResolveAuthor()
    {
        var userId = _currentUserService.GetCurrentUserId();
        return userId == Guid.Empty ? "unknown" : userId.ToString("D");
    }

    private static string ComputeSignature(string author, string version, string payload, string assetsManifest)
    {
        var input = $"{author}|{version}|{payload}|{assetsManifest}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void NormalizePackage(TemplatePackage package)
    {
        package.Version = string.IsNullOrWhiteSpace(package.Version) ? "1.0.0" : package.Version;
        package.Author = string.IsNullOrWhiteSpace(package.Author) ? "unknown" : package.Author;
        if (string.IsNullOrWhiteSpace(package.AssetsManifest))
        {
            package.AssetsManifest = JsonSerializer.Serialize(new TemplateAssetsManifest(), PackageSerializerOptions);
        }
    }

    private static void ValidateOrUpdateSignature(TemplatePackage package)
    {
        var signature = ComputeSignature(package.Author ?? string.Empty, package.Version, package.Payload, package.AssetsManifest ?? string.Empty);
        if (string.IsNullOrWhiteSpace(package.Signature))
        {
            package.Signature = signature;
            return;
        }

        if (!string.Equals(package.Signature, signature, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Template signature mismatch.");
        }
    }

    private static TemplatePayload ParseTemplatePayload(string payload, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Template payload is empty.");
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("tables", out _))
        {
            var spec = JsonSerializer.Deserialize<ModuleSpec>(payload, PackageSerializerOptions) ?? new ModuleSpec();
            EnsureModuleSpecDefaults(spec, fallbackName);
            EnsureTableIds(spec);
            return new TemplatePayload(spec, null);
        }

        if (document.RootElement.TryGetProperty("entityTypes", out _))
        {
            var module = JsonSerializer.Deserialize<S_Module>(payload, PackageSerializerOptions) ?? new S_Module { Name = fallbackName };
            var spec = BuildModuleSpec(module, Array.Empty<STable>());
            EnsureModuleSpecDefaults(spec, fallbackName);
            EnsureTableIds(spec);
            return new TemplatePayload(spec, module);
        }

        var fallback = JsonSerializer.Deserialize<ModuleSpec>(payload, PackageSerializerOptions) ?? new ModuleSpec { Slug = fallbackName };
        EnsureModuleSpecDefaults(fallback, fallbackName);
        EnsureTableIds(fallback);
        return new TemplatePayload(fallback, null);
    }

    private static void EnsureModuleSpecDefaults(ModuleSpec spec, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(spec.Slug))
        {
            spec.Slug = ResolveModuleSlug(fallbackName);
        }

        if (string.IsNullOrWhiteSpace(spec.DisplayName))
        {
            spec.DisplayName = spec.Slug;
        }
    }

    private static void EnsureTableIds(ModuleSpec spec)
    {
        foreach (var table in spec.Tables)
        {
            table.Id ??= Guid.NewGuid();
        }
    }

    private static S_Module BuildModuleFromSpec(ModuleSpec spec, string fallbackName)
    {
        EnsureModuleSpecDefaults(spec, fallbackName);

        var module = new S_Module
        {
            Id = spec.ModuleId ?? Guid.NewGuid(),
            Name = spec.DisplayName ?? spec.Slug,
            Description = spec.Description
        };

        foreach (var table in spec.Tables)
        {
            var fields = table.Fields.Select(MapField).ToList();
            var entity = new S_EntityType
            {
                Id = table.Id ?? Guid.NewGuid(),
                ModuleId = module.Id,
                Name = table.Slug,
                PluralName = table.DisplayName ?? table.Slug,
                Description = table.Description ?? string.Empty,
                Fields = fields
            };

            foreach (var field in fields)
            {
                field.EntityTypeId = entity.Id;
            }

            table.Id = entity.Id;
            module.EntityTypes.Add(entity);
        }

        return module;
    }

    private static void UpsertModuleFromSpec(S_Module module, ModuleSpec spec)
    {
        foreach (var table in spec.Tables)
        {
            var entity = module.EntityTypes.FirstOrDefault(e =>
                (table.Id.HasValue && e.Id == table.Id.Value) ||
                string.Equals(e.Name, table.Slug, StringComparison.OrdinalIgnoreCase));

            if (entity is null)
            {
                entity = new S_EntityType
                {
                    Id = table.Id ?? Guid.NewGuid(),
                    ModuleId = module.Id,
                    Name = table.Slug,
                    PluralName = table.DisplayName ?? table.Slug,
                    Description = table.Description ?? string.Empty
                };
                module.EntityTypes.Add(entity);
            }
            else
            {
                entity.Name = table.Slug;
                entity.PluralName = table.DisplayName ?? table.Slug;
                entity.Description = table.Description ?? entity.Description;
            }

            foreach (var fieldSpec in table.Fields)
            {
                var field = entity.Fields.FirstOrDefault(f =>
                    (fieldSpec.Id.HasValue && f.Id == fieldSpec.Id.Value) ||
                    string.Equals(f.Name, fieldSpec.Slug, StringComparison.OrdinalIgnoreCase));

                if (field is null)
                {
                    field = MapField(fieldSpec);
                    field.EntityTypeId = entity.Id;
                    entity.Fields.Add(field);
                }
                else
                {
                    field.Name = fieldSpec.Slug;
                    field.Label = fieldSpec.Label;
                    field.DataType = ModuleFieldDataTypes.ToDomainType(fieldSpec.DataType);
                    field.IsRequired = fieldSpec.IsRequired;
                    field.IsSearchable = fieldSpec.IsSearchable;
                    field.IsListVisible = fieldSpec.IsListVisible;
                    field.DefaultValue = fieldSpec.DefaultValue?.GetRawText();
                    field.EnumValues = fieldSpec.EnumValues is null ? null : string.Join(",", fieldSpec.EnumValues);
                }
            }
        }
    }

    private static S_Field MapField(FieldSpec spec)
    {
        return new S_Field
        {
            Id = spec.Id ?? Guid.NewGuid(),
            Name = spec.Slug,
            Label = spec.Label,
            DataType = ModuleFieldDataTypes.ToDomainType(spec.DataType),
            IsRequired = spec.IsRequired,
            IsSearchable = spec.IsSearchable,
            IsListVisible = spec.IsListVisible,
            DefaultValue = spec.DefaultValue?.GetRawText(),
            EnumValues = spec.EnumValues is null ? null : string.Join(",", spec.EnumValues)
        };
    }

    private static string ResolveModuleSlug(string name)
        => string.IsNullOrWhiteSpace(name) ? "module" : name.Trim();

    private async Task<TemplatePackage?> TryReadPackageAsync(string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        try
        {
            var package = JsonSerializer.Deserialize<TemplatePackage>(content, PackageSerializerOptions);
            return package is not null && !string.IsNullOrWhiteSpace(package.Payload) ? package : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid ResolveMarketplaceId(string fileName)
    {
        if (Guid.TryParse(fileName, out var parsed))
        {
            return parsed;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fileName));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string MapDataType(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Number or FieldDataType.Int => ModuleFieldDataTypes.Number,
            FieldDataType.Decimal => ModuleFieldDataTypes.Decimal,
            FieldDataType.Boolean => ModuleFieldDataTypes.Boolean,
            FieldDataType.Date => ModuleFieldDataTypes.Date,
            FieldDataType.DateTime => ModuleFieldDataTypes.DateTime,
            FieldDataType.Enum => ModuleFieldDataTypes.Enum,
            FieldDataType.Lookup => ModuleFieldDataTypes.Lookup,
            FieldDataType.File => ModuleFieldDataTypes.File,
            FieldDataType.Note => ModuleFieldDataTypes.Note,
            FieldDataType.Json => ModuleFieldDataTypes.Json,
            FieldDataType.Tags => ModuleFieldDataTypes.Tags,
            _ => ModuleFieldDataTypes.Text
        };
    }

    private static JsonElement? DeserializeDefault(string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(defaultValue);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(defaultValue)).RootElement.Clone();
        }
    }

    private static Dictionary<string, string?>? DeserializeFilter(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(definition);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TemplatePayload(ModuleSpec ModuleSpec, S_Module? Module);
}

