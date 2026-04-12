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

public sealed class DashboardService : IDashboardService
{
    private readonly AionDbContext _db;

    private static readonly JsonSerializerOptions LayoutSerializerOptions = new(JsonSerializerDefaults.Web);

    public DashboardService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<DashboardWidget>> GetWidgetsAsync(CancellationToken cancellationToken = default)
    {
        var widgets = await _db.Widgets.OrderBy(w => w.Order).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (widgets.Count != 0)
        {
            return widgets;
        }

        var defaults = new[]
        {
            new DashboardWidget
            {
                Title = "Rappels agenda",
                WidgetType = DashboardWidgetTypes.AgendaReminders,
                ConfigurationJson = JsonSerializer.Serialize(new DashboardWidgetConfig { MaxItems = 6 }, LayoutSerializerOptions),
                Order = 0
            },
            new DashboardWidget
            {
                Title = "Dernières notes",
                WidgetType = DashboardWidgetTypes.LatestNotes,
                ConfigurationJson = JsonSerializer.Serialize(new DashboardWidgetConfig { MaxItems = 6 }, LayoutSerializerOptions),
                Order = 1
            },
            new DashboardWidget
            {
                Title = "Activité récente",
                WidgetType = DashboardWidgetTypes.RecentActivity,
                ConfigurationJson = JsonSerializer.Serialize(new DashboardWidgetConfig { MaxItems = 6, RangeDays = 7 }, LayoutSerializerOptions),
                Order = 2
            }
        };

        await _db.Widgets.AddRangeAsync(defaults, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return defaults;
    }

    public async Task<DashboardWidget> SaveWidgetAsync(DashboardWidget widget, CancellationToken cancellationToken = default)
    {
        if (await _db.Widgets.AnyAsync(w => w.Id == widget.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.Widgets.Update(widget);
        }
        else
        {
            await _db.Widgets.AddAsync(widget, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return widget;
    }

    public async Task<DashboardLayout?> GetLayoutAsync(string dashboardKey, CancellationToken cancellationToken = default)
        => await _db.DashboardLayouts.FirstOrDefaultAsync(l => l.DashboardKey == dashboardKey, cancellationToken).ConfigureAwait(false);

    public async Task<DashboardLayout> SaveLayoutAsync(DashboardLayout layout, CancellationToken cancellationToken = default)
    {
        layout.UpdatedAt = DateTimeOffset.UtcNow;

        if (await _db.DashboardLayouts.AnyAsync(l => l.Id == layout.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.DashboardLayouts.Update(layout);
        }
        else
        {
            await _db.DashboardLayouts.AddAsync(layout, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return layout;
    }
}

