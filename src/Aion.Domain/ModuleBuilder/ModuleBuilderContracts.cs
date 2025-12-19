using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aion.Domain.ModuleBuilder;

public sealed record ModuleValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ModuleValidationResult Success() => new(true, Array.Empty<string>());
    public static ModuleValidationResult Failure(IEnumerable<string> errors) => new(false, new List<string>(errors));
}

public sealed class ModuleValidationException : InvalidOperationException
{
    public ModuleValidationException(IEnumerable<string> errors)
        : base($"ModuleSpec validation failed: {string.Join("; ", errors)}")
    {
        Errors = new List<string>(errors);
    }

    public IReadOnlyList<string> Errors { get; }
}

public sealed record ModuleSpecDesignResult(ModuleSpec Spec, string RawJson);

public interface IModuleSpecDesigner
{
    Task<ModuleSpecDesignResult> DesignAsync(string prompt, CancellationToken cancellationToken = default);
    string? LastGeneratedJson { get; }
}

public interface IModuleValidator
{
    Task<ModuleValidationResult> ValidateAsync(ModuleSpec spec, CancellationToken cancellationToken = default);
    Task ValidateAndThrowAsync(ModuleSpec spec, CancellationToken cancellationToken = default);
}

public interface IModuleApplier
{
    Task<IReadOnlyList<STable>> ApplyAsync(ModuleSpec spec, CancellationToken cancellationToken = default);
}
