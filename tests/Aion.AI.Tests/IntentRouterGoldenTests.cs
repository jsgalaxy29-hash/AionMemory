using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.AI.Tests;

public class IntentRouterGoldenTests
{
    [Fact]
    public async Task RouteAsync_builds_module_response()
    {
        var module = new S_Module
        {
            Name = "Potager",
            EntityTypes =
            [
                new S_EntityType
                {
                    Name = "Culture",
                    Fields = [new S_Field { Name = "Nom" }, new S_Field { Name = "Variete" }]
                }
            ]
        };

        var router = BuildRouter(moduleDesigner: new StubModuleDesigner(module));
        var result = await router.RouteAsync(new IntentRouteRequest
        {
            Input = "Crée un module pour suivre mes cultures",
            Intent = new IntentDetectionResult(IntentCatalog.Module, new Dictionary<string, string>(), 0.9, "module")
        });

        Assert.Equal(IntentTarget.ModuleDesigner, result.IntentClass.Target);
        Assert.Contains("Module généré: Potager", result.Response, StringComparison.Ordinal);
        Assert.Contains("Culture (2 champs)", result.Response, StringComparison.Ordinal);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task RouteAsync_creates_agenda_event_response()
    {
        var start = new DateTimeOffset(2024, 10, 4, 9, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(1);
        var evt = new S_Event { Title = "Réunion", Start = start, End = end };

        var router = BuildRouter(agendaInterpreter: new StubAgendaInterpreter(evt), agendaService: new StubAgendaService(evt));
        var result = await router.RouteAsync(new IntentRouteRequest
        {
            Input = "Planifie une réunion demain",
            Intent = new IntentDetectionResult(IntentCatalog.Agenda, new Dictionary<string, string>(), 0.8, "agenda")
        });

        Assert.Equal(IntentTarget.AgendaService, result.IntentClass.Target);
        Assert.Contains("Réunion", result.Response, StringComparison.Ordinal);
        Assert.Contains(start.ToString("t"), result.Response, StringComparison.Ordinal);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task RouteAsync_creates_note_response()
    {
        var refined = new S_Note { Title = "Note IA", Content = "Synthèse mock" };

        var router = BuildRouter(noteInterpreter: new StubNoteInterpreter(refined), noteService: new StubNoteService(refined));
        var result = await router.RouteAsync(new IntentRouteRequest
        {
            Input = "Améliore cette note",
            Intent = new IntentDetectionResult(IntentCatalog.Note, new Dictionary<string, string>(), 0.82, "note")
        });

        Assert.Equal(IntentTarget.NoteService, result.IntentClass.Target);
        Assert.Contains("Note créée: Note IA", result.Response, StringComparison.Ordinal);
        Assert.Contains("Synthèse mock", result.Response, StringComparison.Ordinal);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task RouteAsync_builds_report_response()
    {
        var report = new S_ReportDefinition { Name = "Synthèse", Visualization = "list" };
        var moduleId = Guid.NewGuid();

        var router = BuildRouter(reportInterpreter: new StubReportInterpreter(report));
        var result = await router.RouteAsync(new IntentRouteRequest
        {
            Input = "Génère un rapport",
            Intent = new IntentDetectionResult(IntentCatalog.Report, new Dictionary<string, string>(), 0.71, "report"),
            ModuleId = moduleId
        });

        Assert.Equal(IntentTarget.ReportService, result.IntentClass.Target);
        Assert.Contains("Rapport IA: Synthèse", result.Response, StringComparison.Ordinal);
        Assert.Contains("visualisation: list", result.Response, StringComparison.Ordinal);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task RouteAsync_builds_data_engine_response()
    {
        var module = new S_Module
        {
            Name = "Clients",
            EntityTypes = [new S_EntityType { Name = "Client" }]
        };
        var interpretation = new CrudInterpretation(
            "create",
            new Dictionary<string, string?> { ["status"] = "active" },
            new Dictionary<string, string?> { ["name"] = "Ada" },
            "payload");

        var tables = new[]
        {
            new STable { Id = Guid.NewGuid(), Name = "Clients" },
            new STable { Id = Guid.NewGuid(), Name = "Contacts" }
        };

        var router = BuildRouter(crudInterpreter: new StubCrudInterpreter(interpretation), dataEngine: new StubDataEngine(tables));
        var result = await router.RouteAsync(new IntentRouteRequest
        {
            Input = "Ajoute un client",
            Intent = new IntentDetectionResult(IntentCatalog.Data, new Dictionary<string, string>(), 0.77, "data"),
            ModuleContext = module
        });

        Assert.Equal(IntentTarget.DataEngine, result.IntentClass.Target);
        Assert.Contains("DataEngine sélectionné (2 tables)", result.Response, StringComparison.Ordinal);
        Assert.Contains("Action: create", result.Response, StringComparison.Ordinal);
        Assert.Contains("status=active", result.Response, StringComparison.Ordinal);
        Assert.Contains("name=Ada", result.Response, StringComparison.Ordinal);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task RouteAsync_routes_chat_intent_to_chat_model()
    {
        var router = BuildRouter(chatModel: new StubChatModel("Bonjour"));
        var result = await router.RouteAsync(new IntentRouteRequest
        {
            Input = "Salut",
            Intent = new IntentDetectionResult(IntentCatalog.Chat, new Dictionary<string, string>(), 0.6, "chat")
        });

        Assert.Equal(IntentTarget.Chat, result.IntentClass.Target);
        Assert.Equal("Bonjour", result.Response);
        Assert.False(result.UsedFallback);
    }

    private static IntentRouter BuildRouter(
        IModuleDesigner? moduleDesigner = null,
        ICrudInterpreter? crudInterpreter = null,
        IAgendaInterpreter? agendaInterpreter = null,
        INoteInterpreter? noteInterpreter = null,
        IReportInterpreter? reportInterpreter = null,
        IDataEngine? dataEngine = null,
        INoteService? noteService = null,
        IAgendaService? agendaService = null,
        IChatModel? chatModel = null)
    {
        return new IntentRouter(
            moduleDesigner ?? new StubModuleDesigner(new S_Module { Name = "Default" }),
            crudInterpreter ?? new StubCrudInterpreter(new CrudInterpretation("read", new Dictionary<string, string?>(), new Dictionary<string, string?>(), "")),
            agendaInterpreter ?? new StubAgendaInterpreter(new S_Event { Title = "Default", Start = DateTimeOffset.UtcNow }),
            noteInterpreter ?? new StubNoteInterpreter(new S_Note { Title = "Default", Content = "" }),
            reportInterpreter ?? new StubReportInterpreter(new S_ReportDefinition { Name = "Default" }),
            dataEngine ?? new StubDataEngine(Array.Empty<STable>()),
            noteService ?? new StubNoteService(new S_Note { Title = "Default", Content = "" }),
            agendaService ?? new StubAgendaService(new S_Event { Title = "Default", Start = DateTimeOffset.UtcNow }),
            chatModel ?? new StubChatModel("ok"),
            NullLogger<IntentRouter>.Instance);
    }

    private sealed class StubModuleDesigner : IModuleDesigner
    {
        private readonly S_Module _module;

        public StubModuleDesigner(S_Module module)
        {
            _module = module;
        }

        public string? LastGeneratedJson { get; private set; }

        public Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
        {
            LastGeneratedJson = "{}";
            return Task.FromResult(new ModuleDesignResult(_module, LastGeneratedJson));
        }
    }

    private sealed class StubCrudInterpreter : ICrudInterpreter
    {
        private readonly CrudInterpretation _interpretation;

        public StubCrudInterpreter(CrudInterpretation interpretation)
        {
            _interpretation = interpretation;
        }

        public Task<CrudInterpretation> GenerateQueryAsync(CrudQueryRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_interpretation);
    }

    private sealed class StubAgendaInterpreter : IAgendaInterpreter
    {
        private readonly S_Event _event;

        public StubAgendaInterpreter(S_Event evt)
        {
            _event = evt;
        }

        public Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default)
            => Task.FromResult(_event);
    }

    private sealed class StubNoteInterpreter : INoteInterpreter
    {
        private readonly S_Note _note;

        public StubNoteInterpreter(S_Note note)
        {
            _note = note;
        }

        public Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default)
            => Task.FromResult(_note);
    }

    private sealed class StubReportInterpreter : IReportInterpreter
    {
        private readonly S_ReportDefinition _report;

        public StubReportInterpreter(S_ReportDefinition report)
        {
            _report = report;
        }

        public Task<ReportBuildResult> BuildReportAsync(ReportBuildRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ReportBuildResult(_report, "raw"));
    }

    private sealed class StubDataEngine : IDataEngine
    {
        private readonly IReadOnlyCollection<STable> _tables;

        public StubDataEngine(IEnumerable<STable> tables)
        {
            _tables = tables.ToArray();
        }

        public Task<IEnumerable<STable>> GetTablesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<STable>>(_tables);

        public Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<F_Record> InsertAsync(Guid tableId, string dataJson, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<F_Record> InsertAsync(Guid tableId, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<F_Record?> GetAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<ChangeSet>> GetHistoryAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<int> CountAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<F_Record>> QueryAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<RecordSearchHit>> SearchAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<RecordSearchHit>> SearchSmartAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<KnowledgeEdge> LinkRecordsAsync(
            Guid fromTableId,
            Guid fromRecordId,
            Guid toTableId,
            Guid toRecordId,
            KnowledgeRelationType relationType,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<KnowledgeGraphSlice> GetKnowledgeGraphAsync(
            Guid tableId,
            Guid recordId,
            int depth = 1,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubNoteService : INoteService
    {
        private readonly S_Note _note;

        public StubNoteService(S_Note note)
        {
            _note = note;
        }

        public Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_note);

        public Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubAgendaService : IAgendaService
    {
        private readonly S_Event _event;

        public StubAgendaService(S_Event evt)
        {
            _event = evt;
        }

        public Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
            => Task.FromResult(_event);

        public Task<S_Event> UpdateEventAsync(S_Event evt, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<S_Event>> GetOccurrencesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubChatModel : IChatModel
    {
        private readonly string _content;

        public StubChatModel(string content)
        {
            _content = content;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse(_content, _content));
    }
}
