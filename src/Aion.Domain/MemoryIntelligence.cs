using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Aion.Domain;

public sealed record MemoryRecord(
    Guid Id,
    string Title,
    string Content,
    string SourceType,
    Guid? TableId = null,
    DateTimeOffset? Timestamp = null,
    IReadOnlyCollection<string>? Tags = null
);

public sealed record MemoryTopic(string Name, IReadOnlyCollection<string> Keywords);

public sealed record MemoryLinkSuggestion(Guid FromId, Guid ToId, string Reason, string? FromType = null, string? ToType = null);

public readonly record struct MemoryAnalysisRequest
{
    public required IReadOnlyCollection<MemoryRecord> Records { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public string? Scope { get; init; }

    public MemoryAnalysisRequest(IReadOnlyCollection<MemoryRecord> records, string locale = "fr-FR", string? scope = null)
    {
        Records = records ?? throw new ArgumentNullException(nameof(records));
        Locale = locale;
        Scope = scope;
    }
}

public readonly record struct MemoryAnalysisResult(
    string Summary,
    IReadOnlyCollection<MemoryTopic> Topics,
    IReadOnlyCollection<MemoryLinkSuggestion> SuggestedLinks,
    string RawResponse
);

public class MemoryInsight
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyCollection<MemoryTopic> EmptyTopics = Array.Empty<MemoryTopic>();
    private static readonly IReadOnlyCollection<MemoryLinkSuggestion> EmptyLinks = Array.Empty<MemoryLinkSuggestion>();

    public Guid Id { get; set; } = Guid.NewGuid();

    [StringLength(128)]
    public string? Scope { get; set; }

    public int RecordCount { get; set; }

    [Required, StringLength(2048)]
    public string Summary { get; set; } = string.Empty;

    [Required]
    public string TopicsJson { get; set; } = "[]";

    [Required]
    public string SuggestedLinksJson { get; set; } = "[]";

    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [NotMapped]
    public IReadOnlyCollection<MemoryTopic> Topics
    {
        get => Deserialize(TopicsJson, EmptyTopics);
        set => TopicsJson = Serialize(value);
    }

    [NotMapped]
    public IReadOnlyCollection<MemoryLinkSuggestion> SuggestedLinks
    {
        get => Deserialize(SuggestedLinksJson, EmptyLinks);
        set => SuggestedLinksJson = Serialize(value);
    }

    public static MemoryInsight FromAnalysis(MemoryAnalysisResult analysis, string? scope = null, int recordCount = 0)
        => new()
        {
            Scope = scope,
            RecordCount = recordCount,
            Summary = analysis.Summary,
            TopicsJson = Serialize(analysis.Topics),
            SuggestedLinksJson = Serialize(analysis.SuggestedLinks),
            GeneratedAt = DateTimeOffset.UtcNow
        };

    private static string Serialize<T>(IEnumerable<T>? values)
        => JsonSerializer.Serialize(values ?? Array.Empty<T>(), SerializerOptions);

    private static IReadOnlyCollection<T> Deserialize<T>(string json, IReadOnlyCollection<T> fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<T>>(json, SerializerOptions);
            return parsed is null ? fallback : new ReadOnlyCollection<T>(parsed);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }
}
