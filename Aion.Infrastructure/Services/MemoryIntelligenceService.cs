using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class MemoryIntelligenceService : IMemoryIntelligenceService
{
    private readonly AionDbContext _db;
    private readonly IMemoryAnalyzer _analyzer;
    private readonly ILogger<MemoryIntelligenceService> _logger;

    public MemoryIntelligenceService(AionDbContext db, IMemoryAnalyzer analyzer, ILogger<MemoryIntelligenceService> logger)
    {
        _db = db;
        _analyzer = analyzer;
        _logger = logger;
    }

    public async Task<MemoryInsight> AnalyzeAsync(MemoryAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var analysis = await _analyzer.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        var insight = MemoryInsight.FromAnalysis(analysis, request.Scope, request.Records.Count);

        await _db.MemoryInsights.AddAsync(insight, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Memory insight persisted with {RecordCount} records in scope {Scope}", request.Records.Count, request.Scope ?? "global");

        return insight;
    }

    public async Task<IReadOnlyCollection<MemoryInsight>> GetRecentAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);

        var insights = await _db.MemoryInsights
            .OrderByDescending(i => i.GeneratedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return insights;
    }
}
