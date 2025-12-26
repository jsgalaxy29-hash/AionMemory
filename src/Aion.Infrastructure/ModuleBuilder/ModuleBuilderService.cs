using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.ModuleBuilder;

public sealed class ModuleBuilderService
{
    private readonly IModuleSpecDesigner _designer;
    private readonly IModuleValidator _validator;
    private readonly IModuleApplier _applier;
    private readonly ILogger<ModuleBuilderService> _logger;

    public ModuleBuilderService(IModuleSpecDesigner designer, IModuleValidator validator, IModuleApplier applier, ILogger<ModuleBuilderService> logger)
    {
        _designer = designer;
        _validator = validator;
        _applier = applier;
        _logger = logger;
    }

    public string? LastGeneratedJson => _designer.LastGeneratedJson;

    public async Task<ModuleSpecDesignResult> DesignAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var design = await _designer.DesignAsync(prompt, cancellationToken).ConfigureAwait(false);
        var validation = await _validator.ValidateAsync(design.Spec, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            _logger.LogWarning("ModuleSpec generated from prompt is invalid: {Errors}", string.Join(", ", validation.Errors));
            throw new ModuleValidationException(validation.Errors);
        }

        return design;
    }

    public async Task<IReadOnlyList<STable>> DesignAndApplyAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var design = await DesignAsync(prompt, cancellationToken).ConfigureAwait(false);
        return await _applier.ApplyAsync(design.Spec, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
