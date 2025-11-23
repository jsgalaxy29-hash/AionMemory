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
public sealed class DefaultAionAiProvider : ITextGenerationProvider, IEmbeddingProvider, IAudioTranscriptionProvider
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

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            _logger.LogWarning("Endpoint IA non configuré, retour d'une réponse stub.");
            return $"[stub] {_options.Model ?? "model"}: {prompt}";
        }

        var client = CreateClient();
        var payload = new { model = _options.Model ?? "generic-llm", messages = new[] { new { role = "user", content = prompt } } };
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Appel IA non réussi ({Status}) - bascule en mode stub", response.StatusCode);
            return $"[stub-fallback] {prompt}";
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return content ?? string.Empty;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
        }

        var client = CreateClient();
        var payload = new { model = _options.Model ?? "generic-embedding", input = text };
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "embeddings"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return values;
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            return new TranscriptionResult("Transcription stub", TimeSpan.Zero, _options.Model);
        }

        var client = CreateClient();
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(_options.Model ?? "generic-transcription"), "model");

        var response = await client.PostAsync(BuildUri(client, "audio/transcriptions"), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, _options.Model);
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("aion-ai");
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
    private readonly ITextGenerationProvider _provider;

    public DefaultIntentRecognizer(ITextGenerationProvider provider)
    {
        _provider = provider;
    }

    public async Task<string> DetectAsync(string input, CancellationToken cancellationToken = default)
    {
        var prompt = $"Analyse l'intention utilisateur pour: {input}";
        return await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class DefaultModuleDesigner : IModuleDesigner
{
    private readonly ITextGenerationProvider _provider;

    public string? LastGeneratedJson { get; private set; }

    public DefaultModuleDesigner(ITextGenerationProvider provider)
    {
        _provider = provider;
    }

    public async Task<S_Module> GenerateModuleFromPromptAsync(string prompt, CancellationToken cancellationToken = default)
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

        var generationPrompt = $"Propose un module AION (entités/champs) pour: {prompt}. La réponse doit être un JSON valide suivant ce schéma: {schema}";
        LastGeneratedJson = (await _provider.GenerateAsync(generationPrompt, cancellationToken).ConfigureAwait(false))?.Trim();

        if (!string.IsNullOrWhiteSpace(LastGeneratedJson))
        {
            try
            {
                var parsedModule = JsonSerializer.Deserialize<S_Module>(LastGeneratedJson, SerializerOptions);
                if (parsedModule is not null)
                {
                    return parsedModule;
                }
            }
            catch (JsonException)
            {
                // Ignored: we fallback to a minimal module below.
            }
        }

        return new S_Module
        {
            Name = string.IsNullOrWhiteSpace(prompt) ? "Module IA" : prompt,
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
    }
}
