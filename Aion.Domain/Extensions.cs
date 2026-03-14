using System;
using System.Collections.Generic;

namespace Aion.Domain;

public sealed record ExtensionDescriptor(
    string Id,
    string Version,
    IReadOnlyCollection<string> Capabilities);

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ExtensionDescriptorAttribute : Attribute
{
    public ExtensionDescriptorAttribute(string id, string version, params string[] capabilities)
    {
        Id = id;
        Version = version;
        Capabilities = capabilities ?? Array.Empty<string>();
    }

    public string Id { get; }
    public string Version { get; }
    public IReadOnlyCollection<string> Capabilities { get; }
}

public interface IExtensionCatalog
{
    IReadOnlyList<ExtensionDescriptor> GetAvailableExtensions();

    IReadOnlyList<ExtensionDescriptor> GetEnabledExtensions();
}

public interface IExtensionState
{
    bool IsEnabled(string extensionId);

    void SetEnabled(string extensionId, bool enabled);

    IReadOnlyCollection<string> GetDisabledExtensions();
}

public sealed record ExtensionOptions
{
    public string? ExtensionsRootPath { get; set; }

    public IList<string> KnownAssemblies { get; } = new List<string>();
}
