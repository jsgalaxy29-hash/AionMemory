using System;
using System.ComponentModel.DataAnnotations;

namespace Aion.Domain;

public enum TenantKind
{
    Solo,
    Family,
    Pro
}

public static class TenancyDefaults
{
    public static readonly Guid DefaultTenantId = new("2b1d8d3f-6a5e-4bc0-8b18-1cfdb61ad0e3");
    public static readonly Guid DefaultWorkspaceId = new("5e0eaf68-9a0e-4d6e-9a75-c6fbcdbb4a67");
    public static readonly Guid DefaultProfileId = new("bfe7a607-5f63-4f6b-9f2f-2b8d77cde283");

    public const string DefaultTenantName = "Solo";
    public const string DefaultWorkspaceName = "Espace principal";
    public const string DefaultProfileName = "Profil local";
}

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    public TenantKind Kind { get; set; } = TenantKind.Solo;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Workspace> Workspaces { get; set; } = new List<Workspace>();
}

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    [StringLength(1024)]
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}

public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    [Required, StringLength(128)]
    public string DisplayName { get; set; } = string.Empty;
    [StringLength(12)]
    public string? Initials { get; set; }
    [StringLength(16)]
    public string? AccentColor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
