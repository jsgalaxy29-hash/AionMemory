using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

public sealed class TranscriptionMetadataInterpreter : ITranscriptionMetadataInterpreter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IChatModel _provider;
    private readonly ILogger<TranscriptionMetadataInterpreter> _logger;

    public TranscriptionMetadataInterpreter(IChatModel provider, ILogger<TranscriptionMetadataInterpreter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<TranscriptionMetadataResult> ExtractAsync(TranscriptionMetadataRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Analyse la transcription suivante et renvoie uniquement un JSON strict conforme au schéma:
{StructuredJsonSchemas.TranscriptionMetadata.Description}
Transcription: {request.Text}
Locale: {request.Locale ?? "n/a"}
Durée (secondes): {(request.DurationSeconds.HasValue ? request.DurationSeconds.Value.ToString("F2") : "inconnue")}";

        var structured = await StructuredJsonResponseHandler.GetValidJsonAsync(
            _provider,
            prompt,
            StructuredJsonSchemas.TranscriptionMetadata,
            _logger,
            cancellationToken).ConfigureAwait(false);

        if (structured.IsValid && !string.IsNullOrWhiteSpace(structured.Json))
        {
            try
            {
                var metadata = JsonSerializer.Deserialize<TranscriptionMetadataPayload>(structured.Json, SerializerOptions);
                if (metadata is not null)
                {
                    return new TranscriptionMetadataResult(
                        new TranscriptionMetadata(
                            metadata.Language ?? "unknown",
                            metadata.Summary ?? string.Empty,
                            metadata.Keywords ?? Array.Empty<string>(),
                            metadata.DurationSeconds),
                        structured.Json,
                        true);
                }
            }
            catch (JsonException)
            {
            }
        }

        return new TranscriptionMetadataResult(
            new TranscriptionMetadata("unknown", string.Empty, Array.Empty<string>(), request.DurationSeconds),
            structured.Json ?? string.Empty,
            false);
    }

    private sealed class TranscriptionMetadataPayload
    {
        public string? Language { get; set; }
        public string? Summary { get; set; }
        public string[]? Keywords { get; set; }
        public double? DurationSeconds { get; set; }
    }
}
