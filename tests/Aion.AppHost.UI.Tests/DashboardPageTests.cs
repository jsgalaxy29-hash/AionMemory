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
        var tables = Enumerable.Range(1, 7)
            .Select(index => new STable
            {
                Id = Guid.NewGuid(),
                Name = $"table-{index}",
                DisplayName = $"Table {index}"
            })
            .ToList();
        var counts = tables.ToDictionary(t => t.Id, _ => 3);
        var events = Enumerable.Range(1, 8)
            .Select(index => new S_Event
            {
                Title = $"Événement {index}",
                Start = DateTimeOffset.Now.AddDays(index)
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

        Services.AddSingleton<IModuleViewService>(new FakeModuleViewService(tables));
        Services.AddSingleton<IRecordQueryService>(new FakeRecordQueryService(counts));
        Services.AddSingleton<IAgendaService>(new FakeAgendaService(events));
        Services.AddSingleton<INoteService>(new FakeNoteService(notes));
        Services.AddSingleton<IDashboardService>(new FakeDashboardService());

        var cut = RenderComponent<DashboardPage>(parameters => parameters.Add(p => p.Entity, "global"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Table 1", cut.Markup);
            Assert.Contains("Événement 1", cut.Markup);
            Assert.Contains("Note 1", cut.Markup);
            Assert.Contains("Page 1 / 2", cut.Markup);
        });
    }
}
