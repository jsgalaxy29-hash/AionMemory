using Aion.AI.ModuleBuilder;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Xunit;

namespace Aion.AI.Tests;

public class ModuleDesignerServiceTests
{
    [Fact]
    public async Task CreateModuleFromJsonAsync_creates_schema_via_ModuleSchemaService_for_missing_tables()
    {
        var metadata = new RecordingMetadataService();
        var schemaService = new RecordingModuleSchemaService();
        var dataEngine = new RecordingDataEngine();
        var service = new ModuleDesignerService(new StubModuleDesigner(), metadata, schemaService, dataEngine);

        const string json = """
        {
          "module": { "name": "Inventaire", "description": "Gestion stock" },
          "entities": [
            {
              "name": "Article",
              "pluralName": "Articles",
              "fields": [
                { "name": "Nom", "label": "Nom", "type": "text", "required": true },
                { "name": "Prix", "label": "Prix", "type": "decimal" }
              ]
            }
          ]
        }
        """;

        var module = await service.CreateModuleFromJsonAsync(json);

        Assert.Equal("Inventaire", module.Name);
        Assert.Single(metadata.CreatedModules);
        Assert.Single(schemaService.CreatedSpecs);
        Assert.Equal(1, dataEngine.GetTableCalls);
        Assert.Equal(0, dataEngine.CreateTableCalls);

        var spec = schemaService.CreatedSpecs[0];
        Assert.Equal(ModuleSpecVersions.V1, spec.Version);
        Assert.Equal("Inventaire", spec.Slug);
        Assert.Single(spec.Tables);
        Assert.Equal("Article", spec.Tables[0].Slug);
        Assert.Equal(2, spec.Tables[0].Fields.Count);
        Assert.Equal(ModuleFieldDataTypes.Text, spec.Tables[0].Fields[0].DataType);
        Assert.Equal(ModuleFieldDataTypes.Decimal, spec.Tables[0].Fields[1].DataType);
    }

    [Fact]
    public async Task CreateModuleFromJsonAsync_skips_schema_creation_when_table_already_exists()
    {
        var metadata = new RecordingMetadataService();
        var schemaService = new RecordingModuleSchemaService();
        var dataEngine = new RecordingDataEngine
        {
            ExistingTableResolver = _ => new STable { Name = "Article" }
        };
        var service = new ModuleDesignerService(new StubModuleDesigner(), metadata, schemaService, dataEngine);

        const string json = """
        {
          "module": { "name": "Inventaire" },
          "entities": [
            {
              "name": "Article",
              "pluralName": "Articles",
              "fields": [
                { "name": "Nom", "label": "Nom", "type": "text", "required": true }
              ]
            }
          ]
        }
        """;

        await service.CreateModuleFromJsonAsync(json);

        Assert.Single(metadata.CreatedModules);
        Assert.Empty(schemaService.CreatedSpecs);
        Assert.Equal(1, dataEngine.GetTableCalls);
        Assert.Equal(0, dataEngine.CreateTableCalls);
    }

    [Fact]
    public async Task CreateModuleFromPromptAsync_keeps_functional_behavior_and_sets_raw_json()
    {
        var module = new S_Module
        {
            Name = "Potager",
            EntityTypes =
            [
                new S_EntityType
                {
                    Name = "Culture",
                    PluralName = "Cultures",
                    Fields = [new S_Field { Name = "Nom", Label = "Nom", DataType = FieldDataType.Text, IsRequired = true }]
                }
            ]
        };

        var metadata = new RecordingMetadataService();
        var schemaService = new RecordingModuleSchemaService();
        var dataEngine = new RecordingDataEngine();
        var service = new ModuleDesignerService(new StubModuleDesigner(module, "{\"raw\":true}"), metadata, schemaService, dataEngine);

        var created = await service.CreateModuleFromPromptAsync("Créer un module potager");

        Assert.Equal("Potager", created.Name);
        Assert.Equal("{\"raw\":true}", service.LastGeneratedJson);
        Assert.Single(metadata.CreatedModules);
        Assert.Single(schemaService.CreatedSpecs);
        Assert.Equal("Culture", schemaService.CreatedSpecs[0].Tables[0].Slug);
    }

    private sealed class StubModuleDesigner : IModuleDesigner
    {
        private readonly S_Module _module;
        private readonly string _rawJson;

        public StubModuleDesigner(S_Module? module = null, string rawJson = "{}")
        {
            _module = module ?? new S_Module { Name = "Default" };
            _rawJson = rawJson;
        }

        public string? LastGeneratedJson => _rawJson;

        public Task<Aion.Domain.ModuleDesignResult> GenerateModuleAsync(Aion.Domain.ModuleDesignRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new Aion.Domain.ModuleDesignResult(_module, _rawJson));
    }

    private sealed class RecordingMetadataService : IMetadataService
    {
        public List<S_Module> CreatedModules { get; } = new();

        public Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<S_Module>>(CreatedModules);

        public Task<S_Module> CreateModuleAsync(S_Module moduleDefinition, CancellationToken cancellationToken = default)
        {
            CreatedModules.Add(moduleDefinition);
            return Task.FromResult(moduleDefinition);
        }

        public Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default)
            => Task.FromResult(entityType);
    }

    private sealed class RecordingModuleSchemaService : IModuleSchemaService
    {
        public List<ModuleSpec> CreatedSpecs { get; } = new();

        public Task<STable> CreateModuleAsync(ModuleSpec spec, CancellationToken cancellationToken = default)
        {
            CreatedSpecs.Add(spec);

            var tableSpec = spec.Tables[0];
            return Task.FromResult(new STable
            {
                Id = tableSpec.Id ?? Guid.NewGuid(),
                Name = tableSpec.Slug,
                DisplayName = tableSpec.DisplayName,
                Description = tableSpec.Description
            });
        }
    }

    private sealed class RecordingDataEngine : IDataEngine
    {
        public int GetTableCalls { get; private set; }
        public int CreateTableCalls { get; private set; }
        public Func<Guid, STable?> ExistingTableResolver { get; init; } = _ => null;

        public Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
        {
            CreateTableCalls++;
            return Task.FromResult(table);
        }

        public Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
        {
            GetTableCalls++;
            return Task.FromResult(ExistingTableResolver(tableId));
        }

        public Task<IEnumerable<STable>> GetTablesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<F_Record> InsertAsync(Guid tableId, string dataJson, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<F_Record> InsertAsync(Guid tableId, IDictionary<string, object?> data, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<F_Record?> GetAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<ChangeSet>> GetHistoryAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<F_Record>> QueryAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<RecordSearchHit>> SearchAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<RecordSearchHit>> SearchSmartAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<KnowledgeEdge> LinkRecordsAsync(Guid fromTableId, Guid fromRecordId, Guid toTableId, Guid toRecordId, KnowledgeRelationType relationType, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<KnowledgeGraphSlice> GetKnowledgeGraphAsync(Guid tableId, Guid recordId, int depth = 1, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
