using System;
using System.Collections.Generic;
using System.Linq;

namespace Aion.Domain;

public enum FieldDataType
{
    Text,
    Number,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Lookup,
    File,
    Note,
    Json,
    Tags,
    Calculated
}

public enum RelationKind
{
    OneToMany,
    ManyToOne,
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
    GenerateNote
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
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<S_EntityType> EntityTypes { get; set; } = new List<S_EntityType>();
    public ICollection<S_ReportDefinition> Reports { get; set; } = new List<S_ReportDefinition>();
    public ICollection<S_AutomationRule> AutomationRules { get; set; } = new List<S_AutomationRule>();
}

public class S_EntityType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PluralName { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public ICollection<S_Field> Fields { get; set; } = new List<S_Field>();
    public ICollection<S_Relation> Relations { get; set; } = new List<S_Relation>();
}

public class S_Field
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? LookupTarget { get; set; }
    public string? OptionsJson { get; set; }
}

public class S_Relation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityTypeId { get; set; }
    public string FromField { get; set; } = string.Empty;
    public string ToEntity { get; set; } = string.Empty;
    public RelationKind Kind { get; set; }
    public bool IsBidirectional { get; set; }
}

public class S_ReportDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string QueryDefinition { get; set; } = string.Empty;
    public string? Visualization { get; set; }
}

public class S_AutomationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public AutomationTriggerType Trigger { get; set; }
    public string TriggerFilter { get; set; } = string.Empty;
    public ICollection<AutomationCondition> Conditions { get; set; } = new List<AutomationCondition>();
    public ICollection<AutomationAction> Actions { get; set; } = new List<AutomationAction>();
    public bool IsEnabled { get; set; } = true;
}

public class AutomationCondition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AutomationRuleId { get; set; }
    public string Expression { get; set; } = string.Empty;
}

public class AutomationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AutomationRuleId { get; set; }
    public AutomationActionType ActionType { get; set; }
    public string ParametersJson { get; set; } = string.Empty;
}

public class AutomationExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RuleId { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string PayloadSnapshot { get; set; } = string.Empty;
    public AutomationExecutionStatus Status { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public class S_Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public NoteSourceType Source { get; set; }
    public Guid? AudioFileId { get; set; }
    public bool IsTranscribed { get; set; }
    public string? JournalContext { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<J_Note_Link> Links { get; set; } = new List<J_Note_Link>();
}

public class J_Note_Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
}

public class S_Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
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
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
}

public class F_File
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? ThumbnailPath { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class F_Record
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityTypeId { get; set; }
    public string DataJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class S_VisionAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public VisionAnalysisType AnalysisType { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class S_HistoryEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public ICollection<S_Link> Links { get; set; } = new List<S_Link>();
}

public class S_Link
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Relation { get; set; } = string.Empty;
}

public class DashboardWidget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string WidgetType { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = string.Empty;
    public int Order { get; set; }
}

public class TemplatePackage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Payload { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
}

public class MarketplaceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
}

public class PredictionInsight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PredictionKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class UserPersona
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public PersonaTone Tone { get; set; }
    public string? StyleNotes { get; set; }
}

// Métamodèle simplifié : tables, champs et vues permettent d'harmoniser les
// modules AION Memory (Notes, Agenda, Potager, etc.) autour d'une même
// structure persistante.
public class STable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

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
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public FieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? LookupTarget { get; set; }

    public static SFieldDefinition Text(string name, string label, bool required = false, string? defaultValue = null)
        => new()
        {
            Name = name,
            Label = label,
            DataType = FieldDataType.Text,
            IsRequired = required,
            DefaultValue = defaultValue
        };
}

public class SViewDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TableId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string QueryDefinition { get; set; } = string.Empty;
    public string? Visualization { get; set; }
}

public class F_FileLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string? Relation { get; set; }
}
