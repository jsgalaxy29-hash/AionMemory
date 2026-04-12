using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Maui.Storage;

namespace Aion.AppHost.Services;

public sealed class PreferencesExtensionState : IExtensionState
{
    private const string DisabledKey = "aion.extensions.disabled";
    private readonly object _sync = new();
    private HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public bool IsEnabled(string extensionId)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return false;
        }

        EnsureLoaded();
        lock (_sync)
        {
            return !_disabled.Contains(extensionId);
        }
    }

    public void SetEnabled(string extensionId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(extensionId))
        {
            return;
        }

        EnsureLoaded();
        lock (_sync)
        {
            if (enabled)
            {
                _disabled.Remove(extensionId);
            }
            else
            {
                _disabled.Add(extensionId);
            }

            SaveDisabled();
        }
    }

    public IReadOnlyCollection<string> GetDisabledExtensions()
    {
        EnsureLoaded();
        lock (_sync)
        {
            return _disabled.ToArray();
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_sync)
        {
            if (_loaded)
            {
                return;
            }

            var raw = Preferences.Default.Get(DisabledKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(raw);
                    if (parsed is not null)
                    {
                        _disabled = new HashSet<string>(parsed.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (JsonException)
                {
                    _disabled.Clear();
                    Preferences.Default.Remove(DisabledKey);
                }
            }

            _loaded = true;
        }
    }

    private void SaveDisabled()
    {
        if (_disabled.Count == 0)
        {
            Preferences.Default.Remove(DisabledKey);
            return;
        }

        var payload = JsonSerializer.Serialize(_disabled.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        Preferences.Default.Set(DisabledKey, payload);
    }
}
