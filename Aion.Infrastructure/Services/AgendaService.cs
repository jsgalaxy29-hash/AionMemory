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

public sealed class AionAgendaService : IAionAgendaService, IAgendaService
{
    private readonly AionDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AionAgendaService> _logger;

    public AionAgendaService(
        AionDbContext db,
        INotificationService notifications,
        ICurrentUserService currentUserService,
        ILogger<AionAgendaService> logger)
    {
        _db = db;
        _notifications = notifications;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ValidateRecurrence(evt);
        evt.ReminderAt ??= evt.Start.AddHours(-2);
        await _db.Events.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var history = new S_HistoryEvent
        {
            Title = "Évènement planifié",
            Description = evt.Title,
            OccurredAt = DateTimeOffset.UtcNow,
            Links = new List<S_Link>()
        };

        history.ModuleId = await ResolveModuleIdAsync(evt.Links.Select(l => l.TargetType), cancellationToken).ConfigureAwait(false);

        foreach (var link in evt.Links)
        {
            history.Links.Add(new S_Link
            {
                SourceType = nameof(S_HistoryEvent),
                SourceId = history.Id,
                TargetType = link.TargetType,
                TargetId = link.TargetId,
                Relation = "agenda",
                Type = "history",
                CreatedBy = _currentUserService.GetCurrentUserId(),
                Reason = "agenda event"
            });
        }

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Event {EventId} added with reminder {Reminder}", evt.Id, evt.ReminderAt);
        await ScheduleReminderAsync(evt, cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<S_Event> UpdateEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ValidateRecurrence(evt);

        var tracked = await _db.Events.Include(e => e.Links)
            .FirstOrDefaultAsync(e => e.Id == evt.Id, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is null)
        {
            throw new InvalidOperationException($"Event {evt.Id} not found.");
        }

        tracked.Title = evt.Title;
        tracked.Description = evt.Description;
        tracked.Start = evt.Start;
        tracked.End = evt.End;
        tracked.ReminderAt = evt.ReminderAt ?? evt.Start.AddHours(-2);
        tracked.IsCompleted = evt.IsCompleted;
        tracked.RecurrenceFrequency = evt.RecurrenceFrequency;
        tracked.RecurrenceInterval = evt.RecurrenceInterval;
        tracked.RecurrenceCount = evt.RecurrenceCount;
        tracked.RecurrenceUntil = evt.RecurrenceUntil;

        tracked.Links.Clear();
        foreach (var link in evt.Links)
        {
            tracked.Links.Add(new J_Event_Link
            {
                TargetType = link.TargetType,
                TargetId = link.TargetId
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.CancelAsync(tracked.Id, cancellationToken).ConfigureAwait(false);
        await ScheduleReminderAsync(tracked, cancellationToken).ConfigureAwait(false);
        return tracked;
    }

    public async Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var tracked = await _db.Events.Include(e => e.Links)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is null)
        {
            return;
        }

        _db.EventLinks.RemoveRange(tracked.Links);
        _db.Events.Remove(tracked);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.CancelAsync(eventId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => (await _db.Events
            .Where(e => e.Start <= to && (e.Start >= from || (e.RecurrenceFrequency.HasValue && (e.RecurrenceUntil == null || e.RecurrenceUntil >= from))))
            .Include(e => e.Links)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(e => e.Start)
            .ToList();

    public async Task<IEnumerable<S_Event>> GetOccurrencesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var events = await GetEventsAsync(from, to, cancellationToken).ConfigureAwait(false);
        return ExpandOccurrences(events, from, to)
            .OrderBy(e => e.Start)
            .ToList();
    }

    public async Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => await _db.Events.Where(e => !e.IsCompleted && e.ReminderAt.HasValue && e.ReminderAt <= asOf)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static IEnumerable<S_Event> ExpandOccurrences(IEnumerable<S_Event> events, DateTimeOffset from, DateTimeOffset to)
    {
        foreach (var evt in events)
        {
            if (!evt.RecurrenceFrequency.HasValue)
            {
                if (evt.Start >= from && evt.Start <= to)
                {
                    yield return evt;
                }

                continue;
            }

            var interval = evt.RecurrenceInterval.GetValueOrDefault(1);
            if (interval <= 0)
            {
                interval = 1;
            }

            var duration = evt.End.HasValue ? evt.End.Value - evt.Start : (TimeSpan?)null;
            var maxCount = evt.RecurrenceCount ?? int.MaxValue;
            var aligned = AlignToRange(evt.Start, from, evt.RecurrenceFrequency.Value, interval);
            var occurrenceStart = aligned.Start;
            var index = aligned.Skipped;

            while (occurrenceStart <= to && index < maxCount)
            {
                if (!evt.RecurrenceUntil.HasValue || occurrenceStart <= evt.RecurrenceUntil.Value)
                {
                    if (occurrenceStart >= from)
                    {
                        yield return new S_Event
                        {
                            Id = evt.Id,
                            Title = evt.Title,
                            Description = evt.Description,
                            Start = occurrenceStart,
                            End = duration.HasValue ? occurrenceStart.Add(duration.Value) : null,
                            ReminderAt = evt.ReminderAt,
                            RecurrenceFrequency = evt.RecurrenceFrequency,
                            RecurrenceInterval = evt.RecurrenceInterval,
                            RecurrenceCount = evt.RecurrenceCount,
                            RecurrenceUntil = evt.RecurrenceUntil,
                            IsCompleted = evt.IsCompleted,
                            Links = evt.Links.ToList()
                        };
                    }
                }
                else
                {
                    break;
                }

                occurrenceStart = NextOccurrence(occurrenceStart, evt.RecurrenceFrequency.Value, interval);
                index++;
            }
        }
    }

    private static (DateTimeOffset Start, int Skipped) AlignToRange(
        DateTimeOffset start,
        DateTimeOffset from,
        EventRecurrenceFrequency frequency,
        int interval)
    {
        if (start >= from)
        {
            return (start, 0);
        }

        interval = Math.Max(interval, 1);
        return frequency switch
        {
            EventRecurrenceFrequency.Daily => AlignByDays(start, from, interval),
            EventRecurrenceFrequency.Weekly => AlignByDays(start, from, interval * 7),
            EventRecurrenceFrequency.Monthly => AlignByMonths(start, from, interval),
            _ => (start, 0)
        };
    }

    private static (DateTimeOffset Start, int Skipped) AlignByDays(DateTimeOffset start, DateTimeOffset from, int stepDays)
    {
        var totalDays = (from - start).TotalDays;
        var increments = (int)Math.Floor(totalDays / stepDays);
        var aligned = start.AddDays(increments * stepDays);
        var skipped = increments;
        while (aligned < from)
        {
            aligned = aligned.AddDays(stepDays);
            skipped++;
        }

        return (aligned, Math.Max(skipped, 0));
    }

    private static (DateTimeOffset Start, int Skipped) AlignByMonths(DateTimeOffset start, DateTimeOffset from, int stepMonths)
    {
        var monthsDiff = (from.Year - start.Year) * 12 + (from.Month - start.Month);
        var increments = monthsDiff <= 0 ? 0 : monthsDiff / stepMonths;
        var aligned = start.AddMonths(increments * stepMonths);
        var skipped = increments;
        while (aligned < from)
        {
            aligned = aligned.AddMonths(stepMonths);
            skipped++;
        }

        return (aligned, Math.Max(skipped, 0));
    }

    private static DateTimeOffset NextOccurrence(DateTimeOffset current, EventRecurrenceFrequency frequency, int interval)
        => frequency switch
        {
            EventRecurrenceFrequency.Daily => current.AddDays(interval),
            EventRecurrenceFrequency.Weekly => current.AddDays(7 * interval),
            EventRecurrenceFrequency.Monthly => current.AddMonths(interval),
            _ => current
        };

    private static void ValidateRecurrence(S_Event evt)
    {
        if (!evt.RecurrenceFrequency.HasValue)
        {
            return;
        }

        if (evt.RecurrenceInterval.HasValue && evt.RecurrenceInterval <= 0)
        {
            throw new InvalidOperationException("Recurrence interval must be greater than zero.");
        }

        if (evt.RecurrenceCount.HasValue && evt.RecurrenceCount <= 0)
        {
            throw new InvalidOperationException("Recurrence count must be greater than zero.");
        }
    }

    private async Task ScheduleReminderAsync(S_Event evt, CancellationToken cancellationToken)
    {
        if (!evt.ReminderAt.HasValue)
        {
            return;
        }

        await _notifications.ScheduleAsync(
            new NotificationRequest(evt.Id, evt.Title, evt.Description ?? evt.Title, evt.ReminderAt.Value),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveModuleIdAsync(IEnumerable<string> targetTypes, CancellationToken cancellationToken)
    {
        var normalized = targetTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return null;
        }

        var tableIds = await _db.Tables.AsNoTracking()
            .Where(table => normalized.Contains(table.Name))
            .Select(table => table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tableIds.Count == 0)
        {
            return null;
        }

        return await _db.EntityTypes.AsNoTracking()
            .Where(entity => tableIds.Contains(entity.Id))
            .Select(entity => (Guid?)entity.ModuleId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

