using System;
using System.Collections.Generic;
using Aion.Domain;

namespace Aion.Infrastructure.Extensions;

public sealed class DefaultExtensionState : IExtensionState
{
    public bool IsEnabled(string extensionId) => !string.IsNullOrWhiteSpace(extensionId);

    public void SetEnabled(string extensionId, bool enabled)
    {
    }

    public IReadOnlyCollection<string> GetDisabledExtensions() => Array.Empty<string>();
}
