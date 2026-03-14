using System.Text.Json;
using Aion.Domain;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class AutomationOrchestrator : IAutomationOrchestrator
{
    private readonly AionDbContext _db;
    private readonly IAutomationRuleEngine _ruleEngine;
    private readonly ILogger<AutomationOrchestrator> _logger;

    public AutomationOrchestrator(AionDbContext db, IAutomationRuleEngine ruleEngine, ILogger<AutomationOrchestrator> logger)
    {
        _db = db;
        _ruleEngine = ruleEngine;
        _logger = logger;
    }

    public async Task<IEnumerable<AutomationExecution>> TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var automationEvent = new AutomationEvent(
            eventName,
            AutomationTriggerType.Event,
            ToPayloadDictionary(payload));

        var executions = await _ruleEngine.ExecuteAsync(automationEvent, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("[automation] Triggered {Event} -> {Count} execution(s)", eventName, executions.Count);
        return executions;
    }

    public async Task<IEnumerable<AutomationExecution>> GetRecentExecutionsAsync(int take = 50, CancellationToken cancellationToken = default)
        => await _db.AutomationExecutions
            .OrderByDescending(e => e.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static IReadOnlyDictionary<string, object?> ToPayloadDictionary(object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, payload.GetType());
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? new Dictionary<string, object?>();
        }
        catch (Exception)
        {
            return new Dictionary<string, object?> { ["raw"] = payload.ToString() };
        }
    }
}
