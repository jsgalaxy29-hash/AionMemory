using System.Threading;

namespace Aion.Infrastructure.Services.Automation;

internal static class AutomationExecutionContext
{
    private static readonly AsyncLocal<bool> SuppressFlag = new();

    public static bool IsSuppressed => SuppressFlag.Value;

    public static IDisposable Suppress()
    {
        var previous = SuppressFlag.Value;
        SuppressFlag.Value = true;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        public Scope(bool previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            SuppressFlag.Value = _previous;
            _disposed = true;
        }
    }
}
