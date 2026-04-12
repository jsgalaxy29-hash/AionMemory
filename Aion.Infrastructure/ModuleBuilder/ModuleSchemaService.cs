using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.ModuleBuilder;

public sealed class ModuleSchemaService : IModuleSchemaService
{
    private readonly ITableMetadataService _tableMetadataService;
    private readonly IFieldMetadataService _fieldMetadataService;
    private readonly IModuleValidator _moduleValidator;
    private readonly ILogger<ModuleSchemaService> _logger;

    public ModuleSchemaService(
        ITableMetadataService tableMetadataService,
        IFieldMetadataService fieldMetadataService,
        IModuleValidator moduleValidator,
        ILogger<ModuleSchemaService> logger)
    {
        _tableMetadataService = tableMetadataService;
        _fieldMetadataService = fieldMetadataService;
        _moduleValidator = moduleValidator;
        _logger = logger;
    }

    public async Task<STable> CreateModuleAsync(ModuleSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var validation = await _moduleValidator.ValidateAsync(spec, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new ModuleValidationException(validation.Errors);
        }

        if (spec.Tables.Count == 0)
        {
            throw new ModuleValidationException(new[] { "At least one table is required to create a module schema." });
        }

        var primaryTableSpec = spec.Tables[0];
        var fieldNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappedFields = new List<SFieldDefinition>(primaryTableSpec.Fields.Count);

        for (var index = 0; index < primaryTableSpec.Fields.Count; index++)
        {
            var fieldSpec = primaryTableSpec.Fields[index];
            if (!fieldNames.Add(fieldSpec.Slug))
            {
                throw new ModuleValidationException(new[] { $"Field slug '{fieldSpec.Slug}' is duplicated in table '{primaryTableSpec.Slug}'." });
            }

            mappedFields.Add(FieldSpecMapper.Map(fieldSpec, index + 1));
        }

        if (mappedFields.Count == 0)
        {
            throw new ModuleValidationException(new[] { $"Table '{primaryTableSpec.Slug}' must declare at least one field." });
        }

        var table = STable.Create(
            primaryTableSpec.Slug,
            primaryTableSpec.DisplayName ?? primaryTableSpec.Slug,
            Array.Empty<SFieldDefinition>());

        table.Id = primaryTableSpec.Id ?? table.Id;
        table.Description = primaryTableSpec.Description;
        table.IsSystem = primaryTableSpec.IsSystem;
        table.SupportsSoftDelete = primaryTableSpec.SupportsSoftDelete;
        table.HasAuditTrail = primaryTableSpec.HasAuditTrail;
        table.DefaultView = primaryTableSpec.DefaultView;
        table.RowLabelTemplate = primaryTableSpec.RowLabelTemplate;

        await _tableMetadataService.CreateAsync(table, cancellationToken).ConfigureAwait(false);

        foreach (var field in mappedFields)
        {
            await _fieldMetadataService.AddFieldAsync(table.Id, field, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Created module schema for module {ModuleSlug} with table {TableSlug} and {FieldCount} fields.",
            spec.Slug,
            table.Name,
            mappedFields.Count);

        return table;
    }
}
