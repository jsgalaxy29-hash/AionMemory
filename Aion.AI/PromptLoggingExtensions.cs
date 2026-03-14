using Microsoft.Extensions.Logging;

namespace Aion.AI;

internal static class PromptLoggingExtensions
{
    public static string ToSafeLogValue(this string? prompt, AionAiOptions options, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "[empty]";
        }

        var normalized = prompt.Trim();
        if (options.EnablePromptTracing && logger.IsEnabled(LogLevel.Debug))
        {
            return normalized;
        }

        return $"[redacted:length={normalized.Length}]";
    }
}
