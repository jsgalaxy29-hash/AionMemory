using Aion.AppHost.Components.Pages;
using Aion.Domain;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AppHost.UI.Tests;

public class AgendaPageTests : TestContext
{
    [Fact]
    public void Agenda_lists_tracked_events_with_pagination()
    {
        var entity = "AgendaTest";
        var events = Enumerable.Range(1, 12)
            .Select(index => new S_Event
            {
                Title = $"Rendez-vous {index}",
                Start = DateTimeOffset.Now.AddDays(index),
                Links = new List<J_Event_Link>
                {
                    new()
                    {
                        TargetType = entity,
                        TargetId = Guid.NewGuid()
                    }
                }
            })
            .ToList();

        Services.AddSingleton<IAgendaService>(new FakeAgendaService(events));

        var cut = RenderComponent<Agenda>(parameters => parameters.Add(p => p.Entity, entity));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Rendez-vous 1", cut.Markup);
            Assert.Contains("Page 1 / 2", cut.Markup);
        });
    }
}
