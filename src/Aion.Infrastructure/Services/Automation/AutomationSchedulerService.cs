using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Aion.Infrastructure.Services.Automation;

public sealed class AutomationSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutomationSchedulerService> _logger;
    private readonly AutomationSchedulerOptions _options;
    private readonly TimeSpan _interval;
    private DateTimeOffset _lastCheck;

    public AutomationSchedulerService(
        IServiceProvider serviceProvider,
        IOptions<AutomationSchedulerOptions> options,
        ILogger<AutomationSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _interval = TimeSpan.FromSeconds(Math.Max(5, _options.PollingIntervalSeconds));
        _lastCheck = DateTimeOffset.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundServices)
        {
            _logger.LogInformation("Automation scheduler disabled on this platform/configuration.");
            return;
        }

        _lastCheck = DateTimeOffset.UtcNow.Subtract(_interval);
        await RunAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = _lastCheck;
        _lastCheck = now;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AionDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<IAutomationRuleEngine>();

        var scheduledRules = await db.AutomationRules.AsNoTracking()
            .Where(r => r.IsEnabled && r.Trigger == AutomationTriggerType.Scheduled)
            .Select(r => new ScheduledRuleEntry(r.ModuleId, r.TriggerFilter))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var group in scheduledRules
                     .GroupBy(r => new { r.ModuleId, r.TriggerFilter })
                     .OrderBy(g => g.Key.TriggerFilter, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key.TriggerFilter))
            {
                continue;
            }

            if (!TryParseSchedule(group.Key.TriggerFilter, out var schedule))
            {
                continue;
            }

            var windowStartUtc = windowStart.UtcDateTime;
            var nowUtc = now.UtcDateTime;
            var occurrence = schedule.GetNextOccurrence(windowStartUtc);
            while (occurrence <= nowUtc)
            {
                var scheduledAt = new DateTimeOffset(occurrence, TimeSpan.Zero);
                var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["scheduledAt"] = scheduledAt,
                    ["trigger"] = group.Key.TriggerFilter
                };

                var automationEvent = new AutomationEvent(
                    group.Key.TriggerFilter,
                    AutomationTriggerType.Scheduled,
                    payload,
                    group.Key.ModuleId,
                    null);

                await engine.ExecuteAsync(automationEvent, cancellationToken).ConfigureAwait(false);
                occurrence = schedule.GetNextOccurrence(occurrence);
            }
        }
    }

    private bool TryParseSchedule(string triggerFilter, out CrontabSchedule schedule)
    {
        try
        {
            schedule = CrontabSchedule.Parse(triggerFilter, new CrontabSchedule.ParseOptions { IncludingSeconds = false });
            return true;
        }
        catch (CrontabException ex)
        {
            _logger.LogWarning(ex, "Invalid automation schedule: {TriggerFilter}", triggerFilter);
            schedule = null!;
            return false;
        }
    }

    private sealed record ScheduledRuleEntry(Guid ModuleId, string TriggerFilter);
}
