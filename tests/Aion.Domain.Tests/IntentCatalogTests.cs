using Aion.AI;
using Xunit;

namespace Aion.Domain.Tests;

public class IntentCatalogTests
{
    [Theory]
    [InlineData("create_note", IntentCatalog.Note, IntentTarget.NoteService)]
    [InlineData("calendar", IntentCatalog.Agenda, IntentTarget.AgendaService)]
    [InlineData("dataengine", IntentCatalog.Data, IntentTarget.DataEngine)]
    [InlineData("rapport", IntentCatalog.Report, IntentTarget.ReportService)]
    public void Normalize_maps_aliases_to_expected_target(string intent, string expectedName, IntentTarget expectedTarget)
    {
        var intentClass = IntentCatalog.Normalize(intent);

        Assert.Equal(expectedName, intentClass.Name);
        Assert.Equal(expectedTarget, intentClass.Target);
        Assert.True(IntentCatalog.IsKnownName(intent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Normalize_returns_unknown_for_empty_input(string? intent)
    {
        var intentClass = IntentCatalog.Normalize(intent);

        Assert.Equal(IntentCatalog.Unknown, intentClass.Name);
        Assert.Equal(IntentTarget.Unknown, intentClass.Target);
    }
}
