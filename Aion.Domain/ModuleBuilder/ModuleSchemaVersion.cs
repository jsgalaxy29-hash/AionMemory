using System;
using System.Collections.Generic;

namespace Aion.Domain.ModuleBuilder;

public enum ModuleSchemaState
{
    Draft,
    Published
}

public enum ModuleChangeKind
{
    TableAdded,
    FieldAdded,
    FieldRenamed,
    FieldDeactivated,
    ViewAdded,
    ViewUpdated
}

public sealed record ModuleChange(
    ModuleChangeKind Kind,
    string TableSlug,
    string? FieldSlug = null,
    string? PreviousFieldSlug = null,
    string? ViewSlug = null);

public sealed record ChangePlan(
    string ModuleSlug,
    int TargetVersion,
    ModuleSchemaState TargetState,
    IReadOnlyList<ModuleChange> Changes,
    bool HasDestructiveChanges);

public sealed class ModuleSchemaVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ModuleSlug { get; set; } = string.Empty;
    public int Version { get; set; }
    public ModuleSchemaState State { get; set; } = ModuleSchemaState.Draft;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public string SpecHash { get; set; } = string.Empty;
}
