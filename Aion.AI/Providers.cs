using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public interface ITextGenerationProvider
{
    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}

internal static class HttpClientNames
{
    public const string Llm = "aion-ai-llm";
    public const string Embeddings = "aion-ai-embeddings";
    public const string Transcription = "aion-ai-transcription";
    public const string Vision = "aion-ai-vision";
}

public sealed class HttpTextGenerationProvider : ITextGenerationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpTextGenerationProvider> _logger;

    public HttpTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpTextGenerationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = CreateClient(HttpClientNames.Llm, opts.LlmEndpoint ?? opts.BaseEndpoint);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("LLM endpoint not configured; returning stub response");
            return $"[stub-llm] {prompt}";
        }

        var payload = new
        {
            model = opts.LlmModel ?? opts.EmbeddingModel ?? "generic-llm",
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = opts.MaxTokens,
            temperature = opts.Temperature
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LLM call failed with status {Status}; returning stub", response.StatusCode);
            return $"[stub-llm-fallback] {prompt}";
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? string.Empty;
    }

    private HttpClient CreateClient(string clientName, string? endpoint)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        EnsureClientConfigured(client, endpoint, _options.CurrentValue);
        return client;
    }

    internal static void EnsureClientConfigured(HttpClient client, string? endpoint, AionAiOptions options)
    {
        if (client.BaseAddress is null && Uri.TryCreate(endpoint ?? options.BaseEndpoint ?? string.Empty, UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }

        client.Timeout = options.RequestTimeout;

        if (!string.IsNullOrWhiteSpace(options.ApiKey) && client.DefaultRequestHeaders.Authorization is null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        foreach (var header in options.DefaultHeaders)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }
}

public sealed class HttpEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpEmbeddingProvider> _logger;

    public HttpEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpEmbeddingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Embeddings);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.EmbeddingsEndpoint ?? opts.BaseEndpoint, opts);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("Embedding endpoint not configured; returning stub vector");
            return Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
        }

        var payload = new { model = opts.EmbeddingModel ?? opts.LlmModel ?? "generic-embedding", input = text };
        using var request = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Embedding call failed with status {Status}; returning stub", response.StatusCode);
            return Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return values;
    }
}

public sealed class HttpAudioTranscriptionProvider : IAudioTranscriptionProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpAudioTranscriptionProvider> _logger;

    public HttpAudioTranscriptionProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpAudioTranscriptionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Transcription);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.TranscriptionEndpoint ?? opts.BaseEndpoint, opts);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("Transcription endpoint not configured; returning stub transcription");
            return new TranscriptionResult($"[stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel);
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? opts.LlmModel ?? "whisper"), "model");

        var response = await client.PostAsync("audio/transcriptions", content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Transcription call failed with status {Status}; returning stub", response.StatusCode);
            return new TranscriptionResult($"[stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel);
    }
}

public sealed class HttpVisionProvider : IVisionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpVisionProvider> _logger;

    public HttpVisionProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpVisionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<S_VisionAnalysis> AnalyzeAsync(Guid fileId, VisionAnalysisType analysisType, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Vision);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.VisionEndpoint ?? opts.BaseEndpoint, opts);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("Vision endpoint not configured; returning stub analysis");
            return BuildStub(fileId, analysisType, "Unconfigured vision endpoint");
        }

        var payload = new { fileId, analysisType = analysisType.ToString(), model = opts.VisionModel ?? "vision-generic" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "vision/analyze")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Vision call failed with status {Status}; returning stub", response.StatusCode);
            return BuildStub(fileId, analysisType, "Vision call failed");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new S_VisionAnalysis
        {
            FileId = fileId,
            AnalysisType = analysisType,
            ResultJson = json
        };
    }

    private static S_VisionAnalysis BuildStub(Guid fileId, VisionAnalysisType analysisType, string message)
        => new()
        {
            FileId = fileId,
            AnalysisType = analysisType,
            ResultJson = JsonSerializer.Serialize(new { summary = message, tags = new[] { "stub" } }, SerializerOptions)
        };
}

public sealed record IntentRecognitionResult(string Intent, IDictionary<string, string> Parameters, double Confidence, string RawResponse);

public sealed class IntentRecognizer : IIntentDetector
{
    private readonly ITextGenerationProvider _provider;
    private readonly ILogger<IntentRecognizer> _logger;

    public IntentRecognizer(ITextGenerationProvider provider, ILogger<IntentRecognizer> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<string> DetectAsync(string input, CancellationToken cancellationToken = default)
    {
        var structured = await DetectStructuredAsync(input, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(structured, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public async Task<IntentRecognitionResult> DetectStructuredAsync(string input, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Analyse l'intention utilisateur pour l'entrée suivante et réponds uniquement en JSON compact :
{{"intent":"<type>","parameters":{{...}},"confidence":0.0}}

Message: "{input}"
""";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);

        try
        {
            var result = JsonSerializer.Deserialize<IntentRecognitionResultInternal>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result is not null)
            {
                return new IntentRecognitionResult(result.Intent ?? "unknown", result.Parameters ?? new Dictionary<string, string>(), result.Confidence ?? 0.5, response);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse intent response, returning fallback structure");
        }

        return new IntentRecognitionResult("unknown", new Dictionary<string, string> { ["raw"] = input }, 0.1, response);
    }

    private sealed class IntentRecognitionResultInternal
    {
        public string? Intent { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public double? Confidence { get; set; }
    }
}

public sealed class ModuleDesigner : IModuleDesigner
{
    private readonly ITextGenerationProvider _provider;
    private readonly ILogger<ModuleDesigner> _logger;

    public ModuleDesigner(ITextGenerationProvider provider, ILogger<ModuleDesigner> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<S_Module> DesignModuleAsync(string description, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Propose une structure de module AION (entité principale avec 3-5 champs) en JSON :
{{"name":"<module>","entity":"<entity>","fields":[{{"name":"","label":"","type":"text|number|date|bool"}}]}}
Description: {description}
""";

        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var fields = new List<S_Field>
        {
            new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true },
            new() { Name = "CreatedAt", Label = "Créé le", DataType = FieldDataType.DateTime }
        };

        try
        {
            var design = JsonSerializer.Deserialize<ModuleDesignResponse>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (design?.Fields?.Count > 0)
            {
                fields.AddRange(design.Fields.Select(f => new S_Field
                {
                    Name = f.Name ?? "Field",
                    Label = f.Label ?? f.Name ?? "Champ",
                    DataType = MapFieldType(f.Type),
                    IsRequired = f.Required ?? false
                }));
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse module design response, using default fields");
        }

        return new S_Module
        {
            Name = string.IsNullOrWhiteSpace(description) ? "Nouveau module" : description,
            Description = "Généré par Intent Designer",
            EntityTypes = new List<S_EntityType>
            {
                new()
                {
                    Name = "Item",
                    PluralName = "Items",
                    Fields = fields
                }
            }
        };
    }

    private static FieldDataType MapFieldType(string? type) => type?.ToLowerInvariant() switch
    {
        "number" or "int" or "integer" => FieldDataType.Number,
        "decimal" or "float" => FieldDataType.Decimal,
        "date" => FieldDataType.Date,
        "datetime" => FieldDataType.DateTime,
        "bool" or "boolean" => FieldDataType.Boolean,
        _ => FieldDataType.Text
    };

    private sealed class ModuleDesignResponse
    {
        public string? Name { get; set; }
        public string? Entity { get; set; }
        public List<ModuleDesignField> Fields { get; set; } = new();
    }

    private sealed class ModuleDesignField
    {
        public string? Name { get; set; }
        public string? Label { get; set; }
        public string? Type { get; set; }
        public bool? Required { get; set; }
    }
}

public sealed class CrudInterpreter : ICrudInterpreter
{
    private readonly ITextGenerationProvider _provider;
    private readonly ILogger<CrudInterpreter> _logger;

    public CrudInterpreter(ITextGenerationProvider provider, ILogger<CrudInterpreter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<string> GenerateQueryAsync(string intent, S_Module module, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Tu es un assistant qui traduit les requêtes utilisateur en opérations CRUD pour le module suivant:
Module: {module.Name}
Champs: {string.Join(", ", module.EntityTypes.SelectMany(e => e.Fields.Select(f => f.Name)))}

Réponds par une instruction JSON avec {{"action":"create|update|delete|query","filters":{{}},"payload":{{}}}} pour: {intent}
""";

        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("Empty CRUD interpretation, returning stub query");
            return $"{{\"action\":\"query\",\"filters\":{{}},\"payload\":{{}},\"raw\":\"{intent}\"}}";
        }

        return response;
    }
}

public sealed class AgendaInterpreter : IAgendaInterpreter
{
    private readonly ITextGenerationProvider _provider;

    public AgendaInterpreter(ITextGenerationProvider provider)
    {
        _provider = provider;
    }

    public async Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Génère un événement JSON {{"title":"","start":"ISO","end":"ISO|null","reminder":"ISO|null"}} pour: {input}
""";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        try
        {
            var evt = JsonSerializer.Deserialize<AgendaResponse>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (evt is not null)
            {
                return new S_Event
                {
                    Title = evt.Title ?? input,
                    Description = input,
                    Start = ParseDate(evt.Start) ?? DateTimeOffset.UtcNow,
                    End = ParseDate(evt.End),
                    ReminderAt = ParseDate(evt.Reminder)
                };
            }
        }
        catch (JsonException)
        {
        }

        return new S_Event { Title = input, Start = DateTimeOffset.UtcNow };
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var date) ? date : null;

    private sealed class AgendaResponse
    {
        public string? Title { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Reminder { get; set; }
    }
}

public sealed class NoteInterpreter : INoteInterpreter
{
    private readonly ITextGenerationProvider _provider;

    public NoteInterpreter(ITextGenerationProvider provider)
    {
        _provider = provider;
    }

    public async Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Nettoie et synthétise la note suivante. Réponds uniquement avec le texte amélioré.
Titre: {title}
Contenu:
{content}
""";

        var refined = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return new S_Note { Title = title, Content = refined, Source = NoteSourceType.Generated, CreatedAt = DateTimeOffset.UtcNow };
    }
}

public sealed class ReportInterpreter : IReportInterpreter
{
    private readonly ITextGenerationProvider _provider;

    public ReportInterpreter(ITextGenerationProvider provider)
    {
        _provider = provider;
    }

    public async Task<S_ReportDefinition> BuildReportAsync(string description, Guid moduleId, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Construis une requête JSON {{"query":"...","visualization":"table|chart"}} pour un rapport: {description}
""";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);

        try
        {
            var report = JsonSerializer.Deserialize<ReportResponse>(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (report is not null)
            {
                return new S_ReportDefinition
                {
                    ModuleId = moduleId,
                    Name = description,
                    QueryDefinition = report.Query ?? "select *",
                    Visualization = report.Visualization
                };
            }
        }
        catch (JsonException)
        {
        }

        return new S_ReportDefinition { ModuleId = moduleId, Name = description, QueryDefinition = "select *" };
    }

    private sealed class ReportResponse
    {
        public string? Query { get; set; }
        public string? Visualization { get; set; }
    }
}

public sealed class VisionEngine : IAionVisionService
{
    private readonly HttpVisionProvider _visionProvider;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<VisionEngine> _logger;

    public VisionEngine(HttpVisionProvider visionProvider, IFileStorageService fileStorage, ILogger<VisionEngine> logger)
    {
        _visionProvider = visionProvider;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<S_VisionAnalysis> AnalyzeAsync(Guid fileId, VisionAnalysisType analysisType, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await _fileStorage.OpenAsync(fileId, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("Unable to open file {FileId} for vision analysis", fileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open file {FileId}; continuing with remote call", fileId);
        }

        return await _visionProvider.AnalyzeAsync(fileId, analysisType, cancellationToken).ConfigureAwait(false);
    }
}
