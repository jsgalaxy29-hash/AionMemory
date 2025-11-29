namespace Aion.Domain;

public readonly record struct TranscriptionResult(string Text, TimeSpan Duration, string? Model = null);

// Interfaces principales orientées AION. Elles regroupent les services métiers
// et la couche IA afin de clarifier les responsabilités tout en restant faciles
// à remplacer par des implémentations spécifiques (cloud, on-premise, etc.).
public interface IAionDataEngine : IDataEngine;
public interface IAionNoteService : INoteService;
public interface IAionAgendaService : IAgendaService;
public interface IAionAutomationService : IAutomationService;
public interface IAionVisionService : IVisionService;
public interface IAionLifeLogService : ILifeService;
public interface IAionTemplateMarketplaceService : ITemplateService;
public interface IAionPredictionService : IPredictService;
public interface IAionPersonaEngine : IPersonaEngine;

public interface IMetadataService
{
    Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default);
    Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default);
    Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default);
}

public interface IDataEngine
{
    Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default);
    Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<IEnumerable<STable>> GetTablesAsync(CancellationToken cancellationToken = default);
    Task<F_Record> InsertAsync(Guid entityTypeId, string dataJson, CancellationToken cancellationToken = default);
    Task<F_Record?> GetAsync(Guid entityTypeId, Guid id, CancellationToken cancellationToken = default);
    Task<F_Record> UpdateAsync(Guid entityTypeId, Guid id, string dataJson, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid entityTypeId, Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<F_Record>> QueryAsync(Guid entityTypeId, string? filter = null, IDictionary<string, string?>? equals = null, CancellationToken cancellationToken = default);
}

public interface INoteService
{
    Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default);
    Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default);
}

public interface IAgendaService
{
    Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default);
    Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

public interface IFileStorageService
{
    Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default);
    Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default);
    Task<F_FileLink> LinkAsync(Guid fileId, string targetType, Guid targetId, string? relation = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<F_File>> GetForAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default);
}

public interface ICloudBackupService
{
    Task BackupAsync(string encryptedDatabasePath, CancellationToken cancellationToken = default);
    Task RestoreAsync(string destinationPath, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task<BackupManifest> CreateBackupAsync(bool encrypt = false, CancellationToken cancellationToken = default);
}

public interface IRestoreService
{
    Task RestoreLatestAsync(string? destinationPath = null, CancellationToken cancellationToken = default);
}

public interface ILogService
{
    void LogInformation(string message, IDictionary<string, object?>? properties = null);
    void LogWarning(string message, IDictionary<string, object?>? properties = null);
    void LogError(Exception exception, string message, IDictionary<string, object?>? properties = null);
}

public readonly record struct SearchHit(string TargetType, Guid TargetId, string Title, string Snippet, double Score);

public interface ISearchService
{
    Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default);
    Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default);
    Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default);
    Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default);
}

public interface IAutomationService
{
    Task<S_AutomationRule> AddRuleAsync(S_AutomationRule rule, CancellationToken cancellationToken = default);
    Task<IEnumerable<S_AutomationRule>> GetRulesAsync(CancellationToken cancellationToken = default);
}

public interface IAutomationOrchestrator
{
    Task<IEnumerable<AutomationExecution>> TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default);
    Task<IEnumerable<AutomationExecution>> GetRecentExecutionsAsync(int take = 50, CancellationToken cancellationToken = default);
}

public interface IDashboardService
{
    Task<IEnumerable<DashboardWidget>> GetWidgetsAsync(CancellationToken cancellationToken = default);
    Task<DashboardWidget> SaveWidgetAsync(DashboardWidget widget, CancellationToken cancellationToken = default);
}

public interface ITemplateService
{
    Task<TemplatePackage> ExportModuleAsync(Guid moduleId, CancellationToken cancellationToken = default);
    Task<S_Module> ImportModuleAsync(TemplatePackage package, CancellationToken cancellationToken = default);
    Task<IEnumerable<MarketplaceItem>> GetMarketplaceAsync(CancellationToken cancellationToken = default);
}

public interface ILifeService
{
    Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default);
    Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default);
}

public interface IPredictService
{
    Task<IEnumerable<PredictionInsight>> GenerateAsync(CancellationToken cancellationToken = default);
}

public interface IPersonaEngine
{
    Task<UserPersona> GetPersonaAsync(CancellationToken cancellationToken = default);
    Task<UserPersona> SavePersonaAsync(UserPersona persona, CancellationToken cancellationToken = default);
}

public interface IAudioTranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default);
}

public sealed record LlmResponse(string Content, string RawResponse, string? Model = null);

public interface ILLMProvider
{
    Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

public sealed record EmbeddingResult(float[] Vector, string? Model = null, string? RawResponse = null);

public interface IEmbeddingProvider
{
    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

public readonly record struct IntentDetectionRequest
{
    public required string Input { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public IDictionary<string, string?> Context { get; init; } = new Dictionary<string, string?>();
}

public readonly record struct IntentDetectionResult(string Intent, IReadOnlyDictionary<string, string> Parameters, double Confidence, string RawResponse);

public readonly record struct ModuleDesignRequest
{
    public required string Prompt { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public string? ModuleNameHint { get; init; }
    public bool IncludeRelations { get; init; } = true;
}

public readonly record struct ModuleDesignResult(S_Module Module, string RawDesignJson);

public readonly record struct CrudQueryRequest
{
    public required string Intent { get; init; }
    public required S_Module Module { get; init; }
    public string Locale { get; init; } = "fr-FR";
}

public readonly record struct CrudInterpretation(string Action, IReadOnlyDictionary<string, string?> Filters, IReadOnlyDictionary<string, string?> Payload, string RawResponse);

public readonly record struct ReportBuildRequest
{
    public required Guid ModuleId { get; init; }
    public required string Description { get; init; }
    public string? PreferredVisualization { get; init; }
    public string Locale { get; init; } = "fr-FR";
}

public readonly record struct ReportBuildResult(S_ReportDefinition Report, string RawResponse);

public readonly record struct VisionAnalysisRequest
{
    public required Guid FileId { get; init; }
    public VisionAnalysisType AnalysisType { get; init; } = VisionAnalysisType.Ocr;
    public string? Model { get; init; }
    public string Locale { get; init; } = "fr-FR";
}

public interface IIntentDetector
{
    Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default);
}

public interface IModuleDesigner
{
    Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default);
    string? LastGeneratedJson { get; }
}

public interface ICrudInterpreter
{
    Task<CrudInterpretation> GenerateQueryAsync(CrudQueryRequest request, CancellationToken cancellationToken = default);
}

public interface IAgendaInterpreter
{
    Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default);
}

public interface INoteInterpreter
{
    Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default);
}

public interface IReportInterpreter
{
    Task<ReportBuildResult> BuildReportAsync(ReportBuildRequest request, CancellationToken cancellationToken = default);
}

public interface IVisionService
{
    Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default);
}

public static class AiContractExamples
{
    public static IntentDetectionRequest IntentExample => new()
    {
        Input = "Ajoute un rappel demain à 9h pour appeler le client",
        Context = new Dictionary<string, string?> { ["channel"] = "voice" }
    };

    public static ModuleDesignRequest ModuleDesignExample => new()
    {
        Prompt = "Gestion d'un potager",
        ModuleNameHint = "Potager",
        IncludeRelations = true
    };

    public static CrudQueryRequest CrudExample
    {
        get
        {
            var module = new S_Module
            {
                Name = "Contacts",
                EntityTypes =
                [
                    new()
                    {
                        Name = "Contact",
                        PluralName = "Contacts",
                        Fields =
                        [
                            new() { Name = "Nom", Label = "Nom", DataType = FieldDataType.Text },
                            new() { Name = "Email", Label = "Email", DataType = FieldDataType.Text }
                        ]
                    }
                ]
            };
            return new CrudQueryRequest
            {
                Intent = "Trouve les contacts sans email",
                Module = module
            };
        }
    }

    public static ReportBuildRequest ReportExample => new()
    {
        ModuleId = Guid.NewGuid(),
        Description = "Liste hebdomadaire des tâches en retard",
        PreferredVisualization = "table"
    };

    public static VisionAnalysisRequest VisionExample => new()
    {
        FileId = Guid.NewGuid(),
        AnalysisType = VisionAnalysisType.Ocr,
        Model = "ocr-small"
    };
}
