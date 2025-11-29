using System.Text.Json;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class AutomationOrchestrator : IAutomationOrchestrator
{
    private readonly AionDbContext _db;
    private readonly ILogger<AutomationOrchestrator> _logger;

    public AutomationOrchestrator(AionDbContext db, ILogger<AutomationOrchestrator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<AutomationExecution>> TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var rules = await _db.AutomationRules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .Where(r => r.IsEnabled && string.Equals(r.TriggerFilter, eventName, StringComparison.OrdinalIgnoreCase))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var executions = new List<AutomationExecution>();
        foreach (var rule in rules)
        {
            var execution = new AutomationExecution
            {
                RuleId = rule.Id,
                Trigger = eventName,
                PayloadSnapshot = SerializePayload(payload),
                Status = AutomationExecutionStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
                Outcome = ""
            };

            await _db.AutomationExecutions.AddAsync(execution, cancellationToken).ConfigureAwait(false);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var conditionSummary = rule.Conditions.Count == 0
                    ? "no conditions"
                    : string.Join("; ", rule.Conditions.Select(c => c.Expression));

                var actionSummary = rule.Actions.Count == 0
                    ? "no actions"
                    : string.Join(", ", rule.Actions.Select(a => a.ActionType.ToString()));

                execution.Outcome = $"Conditions: {conditionSummary} | Actions: {actionSummary}";
                execution.Status = AutomationExecutionStatus.Succeeded;
                execution.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation("[automation] Rule {Rule} executed for {Event} with actions {Actions}", rule.Name, eventName, actionSummary);
            }
            catch (Exception ex)
            {
                execution.Status = AutomationExecutionStatus.Failed;
                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Outcome = ex.Message;
                _logger.LogWarning(ex, "[automation] Rule {Rule} failed during execution", rule.Name);
            }

            executions.Add(execution);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return executions;
    }

    public async Task<IEnumerable<AutomationExecution>> GetRecentExecutionsAsync(int take = 50, CancellationToken cancellationToken = default)
        => await _db.AutomationExecutions
            .OrderByDescending(e => e.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static string SerializePayload(object payload)
    {
        try
        {
            return JsonSerializer.Serialize(payload, payload.GetType());
        }
        catch (Exception)
        {
            return payload.ToString() ?? string.Empty;
        }
    }
}
