using System.Net.Http.Headers;

namespace Aion.AI;

internal static class AiHttpClientConfigurator
{
    public static void ConfigureClient(HttpClient client, string? endpoint, AionAiOptions options, bool includeOrganizationHeader = false)
    {
        if (client.BaseAddress is null && Uri.TryCreate(endpoint ?? options.BaseEndpoint ?? string.Empty, UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }

        client.Timeout = options.RequestTimeout;

        if (!string.IsNullOrWhiteSpace(options.ApiKey) && client.DefaultRequestHeaders.Authorization is null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }

        if (includeOrganizationHeader && !string.IsNullOrWhiteSpace(options.Organization))
        {
            client.DefaultRequestHeaders.Remove("OpenAI-Organization");
            client.DefaultRequestHeaders.Add("OpenAI-Organization", options.Organization);
        }

        foreach (var header in options.DefaultHeaders)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }
}
