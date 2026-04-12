namespace Aion.Domain;

public sealed record VisionLabel(string Label, double? Confidence = null);

public sealed record VisionModuleSuggestion(
    Guid? ModuleId,
    string ModuleSlug,
    string ModuleName,
    string Reason,
    string? SourceLabel,
    double? Confidence);

public sealed record VisionSuggestionResult(
    IReadOnlyCollection<VisionLabel> Labels,
    IReadOnlyCollection<VisionModuleSuggestion> Suggestions);

public interface IVisionSuggestionService
{
    Task<VisionSuggestionResult> SuggestModulesAsync(S_VisionAnalysis analysis, CancellationToken cancellationToken = default);
}
