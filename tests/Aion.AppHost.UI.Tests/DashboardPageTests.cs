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
        var events = Enumerable.Range(1, 8)
            .Select(index => new S_Event
            {
                Title = $"Événement {index}",
                Start = DateTimeOffset.Now.AddDays(index),
                ReminderAt = DateTimeOffset.Now.AddDays(index).AddHours(-1)
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
                Title = $"Activité {index}",
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
                Title = "Dernières notes",
                WidgetType = DashboardWidgetTypes.LatestNotes,
                ConfigurationJson = "{\"maxItems\":4}",
                Order = 1
            },
            new DashboardWidget
            {
                Title = "Activité récente",
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
            Assert.Contains("Événement 1", cut.Markup);
            Assert.Contains("Note 1", cut.Markup);
            Assert.Contains("Activité 1", cut.Markup);
            Assert.Contains("Rappels agenda", cut.Markup);
            Assert.Contains("Dernières notes", cut.Markup);
            Assert.Contains("Activité récente", cut.Markup);
        });
    }
}
