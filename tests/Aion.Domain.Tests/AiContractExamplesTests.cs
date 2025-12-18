using System.Linq;
using Aion.Domain;
using Xunit;

namespace Aion.Domain.Tests;

public class AiContractExamplesTests
{
    [Fact]
    public void IntentExample_uses_defaults()
    {
        var example = AiContractExamples.IntentExample;

        Assert.Equal("fr-FR", example.Locale);
        Assert.Equal("voice", example.Context["channel"]);
        Assert.False(string.IsNullOrWhiteSpace(example.Input));
    }

    [Fact]
    public void CrudExample_contains_seed_module()
    {
        var request = AiContractExamples.CrudExample;

        Assert.Equal("fr-FR", request.Locale);
        Assert.Equal("Contacts", request.Module.Name);
        Assert.Equal("Trouve les contacts sans email", request.Intent);
        Assert.NotEmpty(request.Module.EntityTypes.SelectMany(e => e.Fields));
    }

    [Fact]
    public void ReportExample_sets_visualization_hint()
    {
        var report = AiContractExamples.ReportExample;

        Assert.Equal("table", report.PreferredVisualization);
        Assert.Equal("fr-FR", report.Locale);
        Assert.NotEqual(Guid.Empty, report.ModuleId);
    }

    [Fact]
    public void VisionExample_has_defaults()
    {
        var vision = AiContractExamples.VisionExample;

        Assert.Equal(VisionAnalysisType.Ocr, vision.AnalysisType);
        Assert.Equal("fr-FR", vision.Locale);
        Assert.NotEqual(Guid.Empty, vision.FileId);
    }
}
