using Aion.AppHost.Components.Pages;
using Aion.AppHost.Services;
using Aion.Domain;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AppHost.UI.Tests;

public class NotesPageTests : TestContext
{
    [Fact]
    public void Notes_page_creates_note_via_service()
    {
        var tableId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var table = new STable
        {
            Id = tableId,
            Name = "Tache",
            DisplayName = "Tâches"
        };
        var noteService = new FakeNoteService();

        Services.AddSingleton<IModuleViewService>(new FakeModuleViewService(new[] { table }));
        Services.AddSingleton<INoteService>(noteService);

        var cut = RenderComponent<Notes>(parameters => parameters
            .Add(p => p.TableId, tableId.ToString())
            .Add(p => p.RecordId, recordId.ToString()));

        cut.Find("input.input").Change("Compte rendu");
        cut.Find("textarea.input").Change("Contenu détaillé");
        cut.Find("button.button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(noteService.Notes);
            Assert.Contains("enregistrée", cut.Markup);
        });
    }
}
