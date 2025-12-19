namespace Aion.Domain;

// Interfaces principales orientées AION. Elles regroupent les services métiers
// afin de clarifier les responsabilités tout en restant faciles à remplacer
// par des implémentations spécifiques (cloud, on-premise, etc.).
public interface IAionDataEngine : IDataEngine;
public interface IAionNoteService : INoteService;
public interface IAionAgendaService : IAgendaService;
public interface IAionAutomationService : IAutomationService;
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
    Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<F_Record> InsertAsync(Guid tableId, string dataJson, CancellationToken cancellationToken = default);
    Task<F_Record> InsertAsync(Guid tableId, IDictionary<string, object?> data, CancellationToken cancellationToken = default);
    Task<F_Record?> GetAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default);
    Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default);
    Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default);
    Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<F_Record>> QueryAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default);
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
    Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default);
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

