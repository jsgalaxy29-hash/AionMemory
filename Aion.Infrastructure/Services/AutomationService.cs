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

public sealed class AutomationService : IAionAutomationService, IAutomationService
{
    private readonly AionDbContext _db;
    private readonly ILogger<AutomationService> _logger;
    private readonly IAutomationOrchestrator _orchestrator;

    public AutomationService(AionDbContext db, ILogger<AutomationService> logger, IAutomationOrchestrator orchestrator)
    {
        _db = db;
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public async Task<S_AutomationRule> AddRuleAsync(S_AutomationRule rule, CancellationToken cancellationToken = default)
    {
        await _db.AutomationRules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Automation rule {Rule} registered", rule.Name);
        return rule;
    }

    public async Task<IEnumerable<S_AutomationRule>> GetRulesAsync(CancellationToken cancellationToken = default)
        => await _db.AutomationRules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<S_AutomationRule> SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Automation rule {ruleId} not found");

        if (rule.IsEnabled == isEnabled)
        {
            return rule;
        }

        rule.IsEnabled = isEnabled;
        _db.AutomationRules.Update(rule);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Automation rule {Rule} updated: enabled={Enabled}", rule.Name, rule.IsEnabled);
        return rule;
    }

    public Task<IEnumerable<AutomationExecution>> TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        => _orchestrator.TriggerAsync(eventName, payload, cancellationToken);
}

