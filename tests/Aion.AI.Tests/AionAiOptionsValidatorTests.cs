using Xunit;

namespace Aion.AI.Tests;

public class AionAiOptionsValidatorTests
{
    private readonly AionAiOptionsValidator _validator = new();

    [Fact]
    public void Allows_local_provider_without_endpoints()
    {
        var options = new AionAiOptions { Provider = AiProviderNames.Local };

        var result = _validator.Validate(string.Empty, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Requires_endpoint_for_http_provider()
    {
        var options = new AionAiOptions { Provider = AiProviderNames.Http, ApiKey = "token" };

        var result = _validator.Validate(string.Empty, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("BaseEndpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Requires_api_key_for_openai()
    {
        var options = new AionAiOptions
        {
            Provider = AiProviderNames.OpenAi,
            BaseEndpoint = "https://api.openai.com/v1"
        };

        var result = _validator.Validate(string.Empty, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Allows_openai_with_default_endpoint_when_api_key_is_present()
    {
        var options = new AionAiOptions
        {
            Provider = AiProviderNames.OpenAi,
            ApiKey = "token"
        };

        var result = _validator.Validate(string.Empty, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Requires_positive_timeout()
    {
        var options = new AionAiOptions
        {
            Provider = AiProviderNames.Http,
            BaseEndpoint = "https://ai.local",
            RequestTimeout = TimeSpan.Zero
        };

        var result = _validator.Validate(string.Empty, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("RequestTimeout", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Accepts_alias_endpoint_and_model()
    {
        var options = new AionAiOptions
        {
            Provider = AiProviderNames.Http,
            Endpoint = "https://ai.alias",
            Model = "stub-model",
            DefaultHeaders = new Dictionary<string, string> { ["X-Test"] = "1" }
        };

        var result = _validator.Validate(string.Empty, options);

        Assert.True(result.Succeeded);
    }
}
