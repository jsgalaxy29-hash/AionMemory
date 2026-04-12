using Microsoft.Extensions.Options;

namespace Aion.AI.Tests;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
