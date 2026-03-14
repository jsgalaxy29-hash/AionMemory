using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class VisionEngine : IVisionModel, IAionVisionService
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
    public async Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await _fileStorage.OpenAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("Unable to open file {FileId} for vision analysis", request.FileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open file {FileId}; continuing with remote call", request.FileId);
        }
        return await _visionProvider.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
