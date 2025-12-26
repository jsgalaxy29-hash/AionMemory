using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.Domain.ModuleBuilder;

public sealed record ModuleDesignAnswer(string QuestionId, string Answer);

public sealed record ModuleDesignQuestion(string Id, string Question, bool IsRequired = true, string? Hint = null);

public sealed record ModuleDesignSource(string Title, string? Url = null, string? Type = null);

public sealed record ModuleDesignRequest
{
    public required string Prompt { get; init; }
    public string Locale { get; init; } = "fr-FR";
    public bool UseSchemaOrg { get; init; }
    public IReadOnlyList<ModuleDesignAnswer> Answers { get; init; } = Array.Empty<ModuleDesignAnswer>();
}

public sealed record ModuleDesignResult(
    ModuleSpec? Spec,
    IReadOnlyList<ModuleDesignQuestion> Questions,
    IReadOnlyList<ModuleDesignSource> Sources,
    string RawJson)
{
    public bool IsComplete => Spec is not null && Questions.Count == 0;
}

public sealed record ModuleDesignApplyResult(ModuleDesignResult Design, IReadOnlyList<STable> Tables);

public interface IModuleDesignService
{
    Task<ModuleDesignResult> DesignModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default);
    Task<ModuleDesignApplyResult> DesignAndApplyAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default);
    string? LastGeneratedJson { get; }
}
