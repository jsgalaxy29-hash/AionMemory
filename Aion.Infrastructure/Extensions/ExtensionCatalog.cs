using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Extensions;

public sealed class ExtensionCatalog : IExtensionCatalog
{
    private readonly ExtensionOptions _options;
    private readonly IExtensionState _extensionState;
    private readonly ILogger<ExtensionCatalog> _logger;
    private readonly Lazy<IReadOnlyList<ExtensionDescriptor>> _descriptors;

    public ExtensionCatalog(
        IOptions<ExtensionOptions> options,
        IExtensionState extensionState,
        ILogger<ExtensionCatalog> logger)
    {
        _options = options.Value;
        _extensionState = extensionState;
        _logger = logger;
        _descriptors = new Lazy<IReadOnlyList<ExtensionDescriptor>>(LoadDescriptors, isThreadSafe: true);
    }

    public IReadOnlyList<ExtensionDescriptor> GetAvailableExtensions() => _descriptors.Value;

    public IReadOnlyList<ExtensionDescriptor> GetEnabledExtensions()
        => _descriptors.Value.Where(descriptor => _extensionState.IsEnabled(descriptor.Id)).ToList();

    private IReadOnlyList<ExtensionDescriptor> LoadDescriptors()
    {
        if (_options.KnownAssemblies.Count == 0)
        {
            return Array.Empty<ExtensionDescriptor>();
        }

        var rootPath = ResolveRootPath(_options.ExtensionsRootPath);
        var descriptors = new List<ExtensionDescriptor>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _options.KnownAssemblies.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!TryResolveAssemblyPath(rootPath, entry!, out var fullPath, out var reason))
            {
                _logger.LogWarning("Extension assembly '{Assembly}' ignored: {Reason}", entry, reason);
                continue;
            }

            if (!seenPaths.Add(fullPath))
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Extension assembly '{Assembly}' was not found at {Path}.", entry, fullPath);
                continue;
            }

            try
            {
                var assembly = LoadAssembly(fullPath);
                descriptors.Add(DescribeAssembly(assembly));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load extension assembly {Path}.", fullPath);
            }
        }

        return descriptors;
    }

    private static string ResolveRootPath(string? configuredRoot)
    {
        var rootPath = string.IsNullOrWhiteSpace(configuredRoot)
            ? AppContext.BaseDirectory
            : configuredRoot;

        return Path.GetFullPath(rootPath);
    }

    private static bool TryResolveAssemblyPath(string rootPath, string entry, out string fullPath, out string reason)
    {
        reason = string.Empty;
        fullPath = string.Empty;

        if (!entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Only .dll assemblies are allowed.";
            return false;
        }

        var candidate = Path.IsPathRooted(entry)
            ? entry
            : Path.Combine(rootPath, entry);

        fullPath = Path.GetFullPath(candidate);

        if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Assembly path must stay within the configured extensions root.";
            return false;
        }

        return true;
    }

    private static Assembly LoadAssembly(string path)
    {
        var assemblyName = AssemblyName.GetAssemblyName(path);
        var existing = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));

        return existing ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }

    private static ExtensionDescriptor DescribeAssembly(Assembly assembly)
    {
        var attribute = assembly.GetCustomAttribute<ExtensionDescriptorAttribute>();
        var name = assembly.GetName();

        return new ExtensionDescriptor(
            attribute?.Id ?? name.Name ?? "unknown",
            attribute?.Version ?? name.Version?.ToString() ?? "0.0.0",
            attribute?.Capabilities ?? Array.Empty<string>());
    }
}
