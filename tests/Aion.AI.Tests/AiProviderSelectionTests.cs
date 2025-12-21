using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.AI.Tests;

public class AiProviderSelectionTests
{
    [Fact]
    public void Selector_marks_inactive_when_no_configuration()
    {
        var selector = new AiProviderSelector(new StubOptionsMonitor(new AionAiOptions()), NullLogger<AiProviderSelector>.Instance);

        var status = selector.GetStatus();

        Assert.False(status.IsConfigured);
        Assert.Equal(AiProviderNames.Inactive, status.ActiveProvider);
    }

    [Fact]
    public void Selector_falls_back_to_mock_for_unknown_provider()
    {
        var selector = new AiProviderSelector(
            new StubOptionsMonitor(new AionAiOptions { Provider = "custom", ApiKey = "token", BaseEndpoint = "http://localhost" }),
            NullLogger<AiProviderSelector>.Instance);

        var provider = selector.ResolveProviderName();

        Assert.Equal(AiProviderNames.Mock, provider);
    }

    [Fact]
    public async Task ModelFactory_uses_mock_provider_when_requested_provider_is_missing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aion:Ai:ApiKey"] = "token",
                ["Aion:Ai:Provider"] = "openai",
                ["Aion:Ai:BaseEndpoint"] = "http://localhost"
            }!)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));
        services.AddAionAi(configuration);

        using var provider = services.BuildServiceProvider();
        var chat = provider.GetRequiredService<IChatModel>();

        var response = await chat.GenerateAsync("ping");

        Assert.StartsWith("[mock-chat]", response.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntentRecognizer_returns_fallback_on_invalid_payload()
    {
        var recognizer = new IntentRecognizer(new StubProvider("not-json"), NullLogger<IntentRecognizer>.Instance);

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "ping" });

        Assert.Equal("unknown", result.Intent);
        Assert.Equal("ping", result.Parameters["raw"]);
    }

    [Fact]
    public async Task IntentRecognizer_returns_fallback_on_provider_timeout()
    {
        var recognizer = new IntentRecognizer(new ThrowingProvider(new TaskCanceledException("timeout")), NullLogger<IntentRecognizer>.Instance);

        var result = await recognizer.DetectAsync(new IntentDetectionRequest { Input = "ping" });

        Assert.Equal("unknown", result.Intent);
        Assert.Contains("timeout", result.Parameters["error"]);
    }

    private sealed class StubOptionsMonitor : IOptionsMonitor<AionAiOptions>
    {
        public StubOptionsMonitor(AionAiOptions value)
        {
            CurrentValue = value;
        }

        public AionAiOptions CurrentValue { get; }
        public AionAiOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<AionAiOptions, string> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class StubProvider : IChatModel
    {
        private readonly string _payload;

        public StubProvider(string payload)
        {
            _payload = payload;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new LlmResponse(_payload, _payload));
        }
    }

    private sealed class ThrowingProvider : IChatModel
    {
        private readonly Exception _exception;

        public ThrowingProvider(Exception exception)
        {
            _exception = exception;
        }

        public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.FromException<LlmResponse>(_exception);
        }
    }
}
