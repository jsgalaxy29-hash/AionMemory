using Aion.AppHost.Components.Pages;
using Aion.AppHost.Services;
using Aion.Domain;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AppHost.UI.Tests;

public class DashboardPageTests : TestContext
{
    [Fact]
    public void Dashboard_sections_render_and_paginate()
    {
        Services.AddAppHostUiDefaults();

        var events = Enumerable.Range(1, 8)
            .Select(index => new S_Event
            {
                Title = $"Evenement {index}",
                Start = DateTimeOffset.Now.AddDays(index),
                ReminderAt = DateTimeOffset.Now.AddHours(-index)
            })
            .ToList();
        var notes = Enumerable.Range(1, 12)
            .Select(index => new S_Note
            {
                Title = $"Note {index}",
                Content = $"Contenu {index}",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-index)
            })
            .ToList();
        var activity = Enumerable.Range(1, 6)
            .Select(index => new S_HistoryEvent
            {
                Title = $"Activite {index}",
                OccurredAt = DateTimeOffset.UtcNow.AddHours(-index)
            })
            .ToList();
        var widgets = new[]
        {
            new DashboardWidget
            {
                Title = "Rappels agenda",
                WidgetType = DashboardWidgetTypes.AgendaReminders,
                ConfigurationJson = "{\"maxItems\":4}",
                Order = 0
            },
            new DashboardWidget
            {
                Title = "Dernieres notes",
                WidgetType = DashboardWidgetTypes.LatestNotes,
                ConfigurationJson = "{\"maxItems\":4}",
                Order = 1
            },
            new DashboardWidget
            {
                Title = "Activite recente",
                WidgetType = DashboardWidgetTypes.RecentActivity,
                ConfigurationJson = "{\"maxItems\":4,\"rangeDays\":7}",
                Order = 2
            }
        };

        Services.AddSingleton<IAgendaService>(new FakeAgendaService(events));
        Services.AddSingleton<INoteService>(new FakeNoteService(notes));
        Services.AddSingleton<ILifeService>(new FakeLifeService(activity));
        Services.AddSingleton<IDashboardService>(new FakeDashboardService(widgets));

        var cut = RenderComponent<DashboardPage>(parameters => parameters.Add(p => p.Entity, "global"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Evenement 8", cut.Markup);
            Assert.Contains("Note 1", cut.Markup);
            Assert.Contains("Activite 1", cut.Markup);
            Assert.Contains("Rappels agenda", cut.Markup);
            Assert.Contains("Dernieres notes", cut.Markup);
            Assert.Contains("Activite recente", cut.Markup);
        });
    }
}
