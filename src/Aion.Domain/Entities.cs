using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Aion.Domain;

public enum FieldDataType
{
    String,
    Int,
    Decimal,
    Date,
    Bool,
    Enum,
    Relation,
    File,
    Note,
    Tags,
    Json,
    DateTime,
    // Aliases for richer UI needs
    Text = String,
    Number = Int,
    Boolean = Bool,
    Lookup = Relation,
}

public enum RelationKind
{
    OneToMany,
    ManyToMany
}

public enum NoteSourceType
{
    Text,
    Voice,
    Generated,
    Imported
}

public enum AutomationTriggerType
{
    OnCreate,
    OnUpdate,
    OnDelete,
    Scheduled,
    Event
}

public enum AutomationActionType
{
    SendNotification,
    CreateRecord,
    UpdateRecord,
    RunScript,
    TriggerWebhook,
    GenerateNote,
    Tag,
    CreateNote,
    ScheduleReminder
}

public enum AutomationExecutionStatus
{
    Scheduled,
    Running,
    Succeeded,
    Failed,
    Skipped
}

public enum VisionAnalysisType
{
    Ocr,
    Classification,
    Tagging
}

public enum PredictionKind
{
    Reminder,
    Trend,
    Risk,
    Opportunity
}

public enum PersonaTone
{
    Neutral,
    Friendly,
    Formal,
    Coach,
    Analytical
}

public class S_Module
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? Description { get; set; }
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
    public ICollection<S_EntityType> EntityTypes { get; set; } = new List<S_EntityType>();
    public ICollection<S_ReportDefinition> Reports { get; set; } = new List<S_ReportDefinition>();
    public ICollection<S_AutomationRule> AutomationRules { get; set; } = new List<S_AutomationRule>();
}

public class S_EntityType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string PluralName { get; set; } = string.Empty;
    [StringLength(64)]
    public string? Icon { get; set; }
    public ICollection<S_Field> Fields { get; set; } = new List<S_Field>();
    public ICollection<S_Relation> Relations { get; set; } = new List<S_Relation>();
    public string Description { get; set; } = string.Empty;
}

public class S_Field
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityTypeId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Label { get; set; } = string.Empty;
    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsListVisible { get; set; }
    [StringLength(1024)]
    public string? DefaultValue { get; set; }
    [StringLength(4000)]
    public string? EnumValues { get; set; }
    public Guid? RelationTargetEntityTypeId { get; set; }
}

public class S_Relation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromEntityTypeId { get; set; }
    public Guid ToEntityTypeId { get; set; }
    public RelationKind Kind { get; set; }
    [Required, StringLength(128)]
    public string RoleName { get; set; } = string.Empty;
}

public class S_ReportDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(4000)]
    public string QueryDefinition { get; set; } = string.Empty;
    [StringLength(128)]
    public string? Visualization { get; set; }
}

public class S_AutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    public AutomationTriggerType Trigger { get; set; }
    [Required, StringLength(256)]
    public string TriggerFilter { get; set; } = string.Empty;
    public ICollection<AutomationCondition> Conditions { get; set; } = new List<AutomationCondition>();
    public ICollection<AutomationAction> Actions { get; set; } = new List<AutomationAction>();
    public bool IsEnabled { get; set; } = true;
}

public class AutomationCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AutomationRuleId { get; set; }
    [Required, StringLength(1024)]
    public string Expression { get; set; } = string.Empty;
}

public class AutomationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AutomationRuleId { get; set; }
    public AutomationActionType ActionType { get; set; }
    [Required, StringLength(4000)]
    public string ParametersJson { get; set; } = string.Empty;
}

public class AutomationExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    [Required, StringLength(128)]
    public string Trigger { get; set; } = string.Empty;
    [Required, StringLength(4000)]
    public string PayloadSnapshot { get; set; } = string.Empty;
    public AutomationExecutionStatus Status { get; set; }
    [Required, StringLength(512)]
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public class S_Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(256)]
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NoteSourceType Source { get; set; }
    public Guid? AudioFileId { get; set; }
    public bool IsTranscribed { get; set; }
    [StringLength(512)]
    public string? JournalContext { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<J_Note_Link> Links { get; set; } = new List<J_Note_Link>();
}

public class J_Note_Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    [Required, StringLength(128)]
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
}

public class S_Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(256)]
    public string Title { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? Description { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public DateTimeOffset? ReminderAt { get; set; }
    public bool IsCompleted { get; set; }
    public ICollection<J_Event_Link> Links { get; set; } = new List<J_Event_Link>();
}

public class J_Event_Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    [Required, StringLength(128)]
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
}

    public class F_File
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required, StringLength(256)]
        public string FileName { get; set; } = string.Empty;
        [Required, StringLength(128)]
        public string MimeType { get; set; } = "application/octet-stream";
        public long Size { get; set; }
        [Required, StringLength(512)]
        public string StoragePath { get; set; } = string.Empty;
        [StringLength(512)]
        public string? ThumbnailPath { get; set; }
        [Required, StringLength(128)]
        public string Sha256 { get; set; } = string.Empty;
        public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
    }

public class F_Record
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("EntityTypeId")]
    public Guid TableId { get; set; }

    [Required]
    public string DataJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;

    public long Version { get; set; } = 1;

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

public class F_RecordIndex
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("EntityTypeId")]
    public Guid TableId { get; set; }

    public Guid RecordId { get; set; }

    [Required, StringLength(128)]
    public string FieldName { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? StringValue { get; set; }

    public decimal? NumberValue { get; set; }

    public DateTimeOffset? DateValue { get; set; }

    public bool? BoolValue { get; set; }
}

public class F_RecordAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("EntityTypeId")]
    public Guid TableId { get; set; }

    public Guid RecordId { get; set; }

    public ChangeType ChangeType { get; set; }

    public long Version { get; set; }

    [Required]
    public string DataJson { get; set; } = string.Empty;

    public string? PreviousDataJson { get; set; }

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class F_RecordEmbedding
{
    [Column("EntityTypeId")]
    public Guid TableId { get; set; }

    public Guid RecordId { get; set; }

    [Required, StringLength(16000)]
    public string Vector { get; set; } = string.Empty;
}

public sealed record LookupResolution(Guid TargetId, string? Label, Guid? TableId = null, string? TableName = null);

public sealed record ResolvedRecord(F_Record Record, IReadOnlyDictionary<string, object?> Data, IReadOnlyDictionary<string, LookupResolution> Lookups);

public enum ChangeType
{
    Create,
    Update,
    Delete
}

public sealed record ChangeSet(Guid TableId, Guid RecordId, ChangeType ChangeType, long Version, DateTimeOffset ChangedAt, string DataJson, string? PreviousDataJson);

public class S_VisionAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public VisionAnalysisType AnalysisType { get; set; }
    [Required]
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class S_HistoryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(256)]
    public string Title { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? Description { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public ICollection<S_Link> Links { get; set; } = new List<S_Link>();
}

public class S_Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    [Required, StringLength(128)]
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    [Required, StringLength(64)]
    public string Relation { get; set; } = string.Empty;
}

public class DashboardWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Title { get; set; } = string.Empty;
    [Required, StringLength(64)]
    public string WidgetType { get; set; } = string.Empty;
    [Required, StringLength(4000)]
    public string ConfigurationJson { get; set; } = string.Empty;
    public int Order { get; set; }
}

public class TemplatePackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [StringLength(512)]
    public string? Description { get; set; }
    [Required]
    public string Payload { get; set; } = string.Empty;
    [Required, StringLength(32)]
    public string Version { get; set; } = "1.0.0";
}

public class MarketplaceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(64)]
    public string Category { get; set; } = string.Empty;
    [Required, StringLength(512)]
    public string PackagePath { get; set; } = string.Empty;
}

public class PredictionInsight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PredictionKind Kind { get; set; }
    [Required, StringLength(512)]
    public string Message { get; set; } = string.Empty;
    [StringLength(128)]
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class UserPersona
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    public PersonaTone Tone { get; set; }
    [StringLength(1024)]
    public string? StyleNotes { get; set; }
}

// Métamodèle simplifié : tables, champs et vues permettent d'harmoniser les
// modules AION Memory (Notes, Agenda, Potager, etc.) autour d'une même
// structure persistante.
// Exemple JSON pour tests rapides :
// {
//   "name": "agenda_event",
//   "displayName": "Événements",
//   "description": "Gestion des événements calendrier",
//   "isSystem": false,
//   "supportsSoftDelete": true,
//   "hasAuditTrail": true,
//   "defaultView": "upcoming",
//   "rowLabelTemplate": "{{title}} ({{start}})",
//   "fields": [],
//   "views": []
// }
public class STable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string DisplayName { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool SupportsSoftDelete { get; set; }
    public bool HasAuditTrail { get; set; }
    [StringLength(128)]
    public string? DefaultView { get; set; }
    [StringLength(256)]
    public string? RowLabelTemplate { get; set; }

    public ICollection<SFieldDefinition> Fields { get; set; } = new List<SFieldDefinition>();
    public ICollection<SViewDefinition> Views { get; set; } = new List<SViewDefinition>();

    public static STable Create(string name, string displayName, IEnumerable<SFieldDefinition> fields)
        => new()
        {
            Name = name,
            DisplayName = displayName,
            Fields = fields.ToList()
        };
}

public class SFieldDefinition
{
    // Exemple JSON :
    // {
    //   "name": "title",
    //   "label": "Titre",
    //   "dataType": "Text",
    //   "isRequired": true,
    //   "isPrimaryKey": false,
    //   "isUnique": true,
    //   "isIndexed": true,
    //   "minLength": 3,
    //   "maxLength": 160,
    //   "validationPattern": "^.+$",
    //   "placeholder": "Titre de l'événement"
    // }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Label { get; set; } = string.Empty;
    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSearchable { get; set; }
    public bool IsListVisible { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsUnique { get; set; }
    public bool IsIndexed { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsSortable { get; set; }
    public bool IsHidden { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsComputed { get; set; }
    [StringLength(1024)]
    public string? DefaultValue { get; set; }
    [StringLength(4000)]
    public string? EnumValues { get; set; }
    public Guid? RelationTargetEntityTypeId { get; set; }
    [StringLength(128)]
    public string? LookupTarget { get; set; }
    [StringLength(128)]
    public string? LookupField { get; set; }
    [StringLength(2048)]
    public string? ComputedExpression { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    [StringLength(512)]
    public string? ValidationPattern { get; set; }
    [StringLength(256)]
    public string? Placeholder { get; set; }
    [StringLength(128)]
    public string? Unit { get; set; }

    public static SFieldDefinition Text(string name, string label, bool required = false, string? defaultValue = null)
        => new()
        {
            Name = name,
            Label = label,
            DataType = FieldDataType.Text,
            IsRequired = required,
            DefaultValue = defaultValue,
            IsSearchable = true,
            IsListVisible = true
        };
}

public class SViewDefinition
{
    // Exemple JSON :
    // {
    //   "name": "upcoming",
    //   "displayName": "À venir",
    //   "description": "Événements à venir dans les 30 jours",
    //   "queryDefinition": "start >= now() && start < now().addDays(30)",
    //   "filterExpression": "status != \"done\"",
    //   "sortExpression": "start asc, priority desc",
    //   "pageSize": 50,
    //   "visualization": "table",
    //   "isDefault": true
    // }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string DisplayName { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? Description { get; set; }
    [Required, StringLength(4000)]
    public string QueryDefinition { get; set; } = string.Empty;
    [StringLength(512)]
    public string? FilterExpression { get; set; }
    [StringLength(512)]
    public string? SortExpression { get; set; }
    [Range(1, 500)]
    public int? PageSize { get; set; }
    [StringLength(128)]
    public string? Visualization { get; set; }
    public bool IsDefault { get; set; }
}

public class F_FileLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    [Required, StringLength(128)]
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    [StringLength(64)]
    public string? Relation { get; set; }
}
