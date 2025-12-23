using System.Collections.ObjectModel;
using System.Linq;
using Aion.Domain;

namespace Aion.AI;

/// <summary>
/// Domain-facing AI orchestration contracts kept provider-neutral for DataEngine and UI layers.
/// </summary>
public readonly record struct IntentDetectionRequest
{
    public required string Input { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public IDictionary<string, string?> Context { get; init; } = new Dictionary<string, string?>();

    public IntentDetectionRequest(string input, string locale = "fr-FR", IDictionary<string, string?>? context = null)
    {
        Input = input;
        Locale = locale;
        Context = context ?? new Dictionary<string, string?>();
    }
}

public readonly record struct IntentDetectionResult(string Intent, IReadOnlyDictionary<string, string> Parameters, double Confidence, string RawResponse);

public readonly record struct ModuleDesignRequest
{
    public required string Prompt { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public string? ModuleNameHint { get; init; }
    public bool IncludeRelations { get; init; } = true;

    public ModuleDesignRequest(string prompt, string locale = "fr-FR", string? moduleNameHint = null, bool includeRelations = true)
    {
        Prompt = prompt;
        Locale = locale;
        ModuleNameHint = moduleNameHint;
        IncludeRelations = includeRelations;
    }
}

public readonly record struct ModuleDesignResult(S_Module Module, string RawDesignJson);

public readonly record struct CrudQueryRequest
{
    public required string Intent { get; init; }
    public required S_Module Module { get; init; }
    public string Locale { get; init; } = "fr-FR";

    public CrudQueryRequest(string intent, S_Module module, string locale = "fr-FR")
    {
        Intent = intent;
        Module = module;
        Locale = locale;
    }
}

public readonly record struct CrudInterpretation(string Action, IReadOnlyDictionary<string, string?> Filters, IReadOnlyDictionary<string, string?> Payload, string RawResponse);

public readonly record struct ReportBuildRequest
{
    public required Guid ModuleId { get; init; }
    public required string Description { get; init; }
    public string? PreferredVisualization { get; init; }
    public string Locale { get; init; } = "fr-FR";

    public ReportBuildRequest(Guid moduleId, string description, string? preferredVisualization = null, string locale = "fr-FR")
    {
        ModuleId = moduleId;
        Description = description;
        PreferredVisualization = preferredVisualization;
        Locale = locale;
    }
}

public readonly record struct ReportBuildResult(S_ReportDefinition Report, string RawResponse);

public sealed record MemoryContextRequest
{
    public string Query { get; init; } = string.Empty;
    public string Locale { get; init; } = "fr-FR";
    public int RecordLimit { get; init; } = 6;
    public int HistoryLimit { get; init; } = 4;
    public int InsightLimit { get; init; } = 3;
}

public sealed record MemoryContextItem(
    Guid RecordId,
    string SourceType,
    string Title,
    string Snippet,
    DateTimeOffset? Timestamp = null,
    Guid? TableId = null,
    string? Scope = null,
    double Score = 0
);

public sealed record MemoryContextResult(
    IReadOnlyCollection<MemoryContextItem> Records,
    IReadOnlyCollection<MemoryContextItem> History,
    IReadOnlyCollection<MemoryContextItem> Insights)
{
    public bool IsEmpty => Records.Count == 0 && History.Count == 0 && Insights.Count == 0;
    public IReadOnlyCollection<MemoryContextItem> All => new ReadOnlyCollection<MemoryContextItem>(Records.Concat(History).Concat(Insights).ToList());
}

public readonly record struct AssistantAnswerRequest
{
    public required string Question { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public MemoryContextRequest Context { get; init; } = new();

    public AssistantAnswerRequest(string question, string locale = "fr-FR", MemoryContextRequest? context = null)
    {
        Question = question;
        Locale = locale;

        var resolvedContext = context ?? new MemoryContextRequest();
        if (string.IsNullOrWhiteSpace(resolvedContext.Query))
        {
            resolvedContext = resolvedContext with { Query = question, Locale = locale };
        }
        else if (string.IsNullOrWhiteSpace(resolvedContext.Locale))
        {
            resolvedContext = resolvedContext with { Locale = locale };
        }

        Context = resolvedContext;
    }
}

public sealed record AssistantAnswer(
    string Message,
    IReadOnlyCollection<Guid> Citations,
    MemoryContextResult Context,
    string RawResponse,
    bool UsedFallback = false
);

public readonly record struct VisionAnalysisRequest
{
    public required Guid FileId { get; init; }
    public VisionAnalysisType AnalysisType { get; init; } = VisionAnalysisType.Ocr;
    public string? Model { get; init; }
    public string Locale { get; init; } = "fr-FR";

    public VisionAnalysisRequest(Guid fileId, VisionAnalysisType analysisType = VisionAnalysisType.Ocr, string? model = null, string locale = "fr-FR")
    {
        FileId = fileId;
        AnalysisType = analysisType;
        Model = model;
        Locale = locale;
    }
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

public interface IMemoryAnalyzer
{
    Task<MemoryAnalysisResult> AnalyzeAsync(MemoryAnalysisRequest request, CancellationToken cancellationToken = default);
}

public interface IMemoryContextBuilder
{
    Task<MemoryContextResult> BuildAsync(MemoryContextRequest request, CancellationToken cancellationToken = default);
}

public interface IChatAnswerer
{
    Task<AssistantAnswer> AnswerAsync(AssistantAnswerRequest request, CancellationToken cancellationToken = default);
}

public interface IAionVisionService : IVisionService
{
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
