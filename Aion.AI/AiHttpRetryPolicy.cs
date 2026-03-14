using System.Net;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

internal static class AiHttpRetryPolicy
{
    private const int MaxAttempts = 3;

    internal static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        ILogger logger,
        string operationName,
        string providerName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var request = requestFactory();
            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode || !ShouldRetry(response.StatusCode))
            {
                return response;
            }

            response.Dispose();
            AiMetrics.RecordRetry(operationName, providerName);

            var delay = GetDelay(response.Headers.RetryAfter?.Delta, attempt);
            logger.LogWarning("AI request throttled (status {Status}); retrying in {Delay}s", response.StatusCode, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan GetDelay(TimeSpan? retryAfter, int attempt)
    {
        if (retryAfter.HasValue)
        {
            return retryAfter.Value;
        }

        var jitterMs = Random.Shared.Next(100, 500);
        var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        return baseDelay + TimeSpan.FromMilliseconds(jitterMs);
    }
}
