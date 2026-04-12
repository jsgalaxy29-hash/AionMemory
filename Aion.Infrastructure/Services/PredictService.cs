using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class PredictService : IAionPredictionService, IPredictService
{
    private readonly AionDbContext _db;

    public PredictService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<PredictionInsight>> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var insights = new List<PredictionInsight>
        {
            new()
            {
                Kind = PredictionKind.Reminder,
                Message = "Hydrate your potager seedlings this evening.",
                GeneratedAt = DateTimeOffset.UtcNow
            }
        };

        await _db.Predictions.AddRangeAsync(insights, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return insights;
    }
}

