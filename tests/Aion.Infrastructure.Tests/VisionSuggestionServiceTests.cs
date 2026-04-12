using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class VisionSuggestionServiceTests
{
    [Fact]
    public async Task SuggestModulesAsync_MapsLabelsToModule()
    {
        var module = new S_Module { Id = Guid.NewGuid(), Name = "Finances" };
        var metadata = new StubMetadataService(module);
        var service = new VisionSuggestionService(metadata, NullLogger<VisionSuggestionService>.Instance);
        var vision = new HttpMockVisionModel();

        var analysis = await vision.AnalyzeAsync(new VisionAnalysisRequest(Guid.NewGuid(), VisionAnalysisType.Classification));
        var result = await service.SuggestModulesAsync(analysis);

        Assert.NotEmpty(result.Labels);
        Assert.Contains(result.Suggestions, suggestion => suggestion.ModuleId == module.Id && suggestion.ModuleSlug == module.Name);
    }

    private sealed class StubMetadataService : IMetadataService
    {
        private readonly IReadOnlyCollection<S_Module> _modules;

        public StubMetadataService(params S_Module[] modules)
        {
            _modules = modules;
        }

        public Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<S_Module>>(_modules);

        public Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
