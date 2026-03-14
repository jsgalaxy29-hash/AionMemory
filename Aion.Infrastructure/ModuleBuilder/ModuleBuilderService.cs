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
    private readonly IModuleSchemaService _moduleSchemaService;
    private readonly ILogger<ModuleBuilderService> _logger;

    public ModuleBuilderService(IModuleSpecDesigner designer, IModuleValidator validator, IModuleSchemaService moduleSchemaService, ILogger<ModuleBuilderService> logger)
    {
        _designer = designer;
        _validator = validator;
        _moduleSchemaService = moduleSchemaService;
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
        var createdTable = await _moduleSchemaService.CreateModuleAsync(design.Spec, cancellationToken).ConfigureAwait(false);
        return new[] { createdTable };
    }
}
