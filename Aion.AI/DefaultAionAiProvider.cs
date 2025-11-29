using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

/// <summary>
/// Provider générique prêt à être branché sur un endpoint LLM compatible (OpenAI-like).
/// Les appels réseau sont encapsulés dans HttpClientFactory pour être facilement testés
/// et remplacés.
/// </summary>
public sealed class DefaultAionAiProvider : ILLMProvider, IEmbeddingProvider, IAudioTranscriptionProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AionAiOptions _options;
    private readonly ILogger<DefaultAionAiProvider> _logger;

    public DefaultAionAiProvider(IHttpClientFactory httpClientFactory, IOptions<AionAiOptions> options, ILogger<DefaultAionAiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.LlmEndpoint ?? _options.BaseEndpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Endpoint IA non configuré, retour d'une réponse stub.");
            var content = $"[stub] {_options.LlmModel ?? "model"}: {prompt}";
            return new LlmResponse(content, content, _options.LlmModel);
        }

        var client = CreateClient();
        if (client.BaseAddress is null && Uri.TryCreate(endpoint, UriKind.Absolute, out var llmUri))
        {
            client.BaseAddress = llmUri;
        }

        var payload = new { model = _options.LlmModel ?? "generic-llm", messages = new[] { new { role = "user", content = prompt } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Appel IA non réussi ({Status}) - bascule en mode stub", response.StatusCode);
            var content = $"[stub-fallback] {prompt}";
            return new LlmResponse(content, content, _options.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return new LlmResponse(content ?? string.Empty, json, _options.LlmModel);
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.EmbeddingsEndpoint ?? _options.BaseEndpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Embedding endpoint not configured, returning deterministic stub vector.");
            var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector, _options.EmbeddingModel ?? _options.LlmModel);
        }

        var client = CreateClient();
        if (client.BaseAddress is null && Uri.TryCreate(endpoint, UriKind.Absolute, out var embeddingUri))
        {
            client.BaseAddress = embeddingUri;
        }

        var payload = new { model = _options.EmbeddingModel ?? _options.LlmModel ?? "generic-embedding", input = text };
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "embeddings"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new EmbeddingResult(values, _options.EmbeddingModel ?? _options.LlmModel, json);
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.TranscriptionEndpoint ?? _options.BaseEndpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Transcription endpoint not configured, returning stub result.");
            return new TranscriptionResult("Transcription stub", TimeSpan.Zero, _options.TranscriptionModel ?? _options.LlmModel);
        }

        var client = CreateClient();
        if (client.BaseAddress is null && Uri.TryCreate(endpoint, UriKind.Absolute, out var transcriptionUri))
        {
            client.BaseAddress = transcriptionUri;
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(_options.TranscriptionModel ?? _options.LlmModel ?? "generic-transcription"), "model");

        var response = await client.PostAsync(BuildUri(client, "audio/transcriptions"), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, _options.TranscriptionModel ?? _options.LlmModel);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("aion-ai");
        if (client.BaseAddress is null && Uri.TryCreate(_options.LlmEndpoint ?? _options.BaseEndpoint ?? string.Empty, UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiKey) && client.DefaultRequestHeaders.Authorization is null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        return client;
    }

    private static Uri BuildUri(HttpClient client, string relativePath)
    {
        if (client.BaseAddress is null)
        {
            return new Uri(relativePath, UriKind.RelativeOrAbsolute);
        }

        return new Uri(client.BaseAddress, relativePath);
    }
}

public sealed class DefaultIntentRecognizer : IIntentDetector
{
    private readonly ILLMProvider _provider;

    public DefaultIntentRecognizer(ILLMProvider provider)
    {
        _provider = provider;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $"Analyse l'intention utilisateur pour: {request.Input}";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return new IntentDetectionResult("analysis", new Dictionary<string, string> { ["prompt"] = request.Input }, 0.5, response.RawResponse ?? response.Content);
    }
}

public sealed class DefaultModuleDesigner : IModuleDesigner
{
    private readonly ILLMProvider _provider;

    public string? LastGeneratedJson { get; private set; }

    public DefaultModuleDesigner(ILLMProvider provider)
    {
        _provider = provider;
    }

    public async Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var schema = """
{
  "Name": "Nom du module",
  "Description": "Description détaillée",
  "EntityTypes": [
    {
      "Name": "NomEntite",
      "PluralName": "NomsEntites",
      "Fields": [
        {
          "Name": "NomChamp",
          "Label": "Label Champ",
          "DataType": "Text|Number|Decimal|Date|DateTime|Boolean|Lookup|Tags|File|Note"
        }
      ]
    }
  ]
}
""";

        var generationPrompt = $"Propose un module AION (entités/champs) pour: {request.Prompt}. La réponse doit être un JSON valide suivant ce schéma: {schema}";
        LastGeneratedJson = (await _provider.GenerateAsync(generationPrompt, cancellationToken).ConfigureAwait(false)).Content.Trim();

        if (!string.IsNullOrWhiteSpace(LastGeneratedJson))
        {
            try
            {
                var parsedModule = JsonSerializer.Deserialize<S_Module>(LastGeneratedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (parsedModule is not null)
                {
                    return new ModuleDesignResult(parsedModule, LastGeneratedJson);
                }
            }
            catch (JsonException)
            {
                // Ignored: we fallback to a minimal module below.
            }
        }

        var fallback = new S_Module
        {
            Name = string.IsNullOrWhiteSpace(request.ModuleNameHint) ? (string.IsNullOrWhiteSpace(request.Prompt) ? "Module IA" : request.Prompt) : request.ModuleNameHint,
            Description = "Module généré automatiquement",
            EntityTypes = new List<S_EntityType>()
            {
                new()
                {
                    Name = "Item",
                    PluralName = "Items",
                    Fields = new List<S_Field>
                    {
                        new() { Name = "Titre", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true }
                    }
                }
            }
        };
        return new ModuleDesignResult(fallback, LastGeneratedJson ?? string.Empty);
    }
}
