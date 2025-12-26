using System.Text.Json;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services.Automation;

public sealed class AutomationRuleEngine : IAutomationRuleEngine
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AionDbContext _db;
    private readonly INoteService _noteService;
    private readonly IAgendaService _agendaService;
    private readonly Func<IAionDataEngine> _dataEngineFactory;
    private readonly ILogger<AutomationRuleEngine> _logger;

    public AutomationRuleEngine(
        AionDbContext db,
        INoteService noteService,
        IAgendaService agendaService,
        Func<IAionDataEngine> dataEngineFactory,
        ILogger<AutomationRuleEngine> logger)
    {
        _db = db;
        _noteService = noteService;
        _agendaService = agendaService;
        _dataEngineFactory = dataEngineFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<AutomationExecution>> ExecuteAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automationEvent);

        var rules = await LoadRulesAsync(automationEvent, cancellationToken).ConfigureAwait(false);
        var executions = new List<AutomationExecution>();
        var payloadSnapshot = SerializePayload(automationEvent.Payload);

        foreach (var rule in rules)
        {
            var existingExecution = await FindExistingExecutionAsync(rule.Id, payloadSnapshot, cancellationToken).ConfigureAwait(false);
            if (existingExecution is not null)
            {
                executions.Add(existingExecution);
                continue;
            }

            var execution = new AutomationExecution
            {
                RuleId = rule.Id,
                Trigger = automationEvent.Name,
                PayloadSnapshot = payloadSnapshot,
                Status = AutomationExecutionStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
                Outcome = string.Empty
            };

            await _db.AutomationExecutions.AddAsync(execution, cancellationToken).ConfigureAwait(false);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!EvaluateConditions(rule, automationEvent.Payload))
                {
                    execution.Status = AutomationExecutionStatus.Skipped;
                    execution.Outcome = "Conditions not met";
                }
                else
                {
                    var outcomes = new List<string>();

                    foreach (var action in rule.Actions.OrderBy(a => a.Id))
                    {
                        var actionOutcome = await ExecuteActionAsync(action, automationEvent, cancellationToken).ConfigureAwait(false);
                        outcomes.Add(actionOutcome);
                    }

                    execution.Status = AutomationExecutionStatus.Succeeded;
                    execution.Outcome = string.Join(" | ", outcomes);
                }
            }
            catch (Exception ex)
            {
                execution.Status = AutomationExecutionStatus.Failed;
                execution.Outcome = ex.Message;
                _logger.LogWarning(ex, "[automation] Rule {Rule} failed during execution", rule.Name);
            }
            finally
            {
                execution.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            executions.Add(execution);
        }

        return executions;
    }

    private async Task<string> ExecuteActionAsync(AutomationAction action, AutomationEvent automationEvent, CancellationToken cancellationToken)
        => action.ActionType switch
        {
            AutomationActionType.Tag => await ApplyTagAsync(action, automationEvent, cancellationToken).ConfigureAwait(false),
            AutomationActionType.CreateNote or AutomationActionType.GenerateNote => await CreateNoteAsync(action, automationEvent, cancellationToken).ConfigureAwait(false),
            AutomationActionType.ScheduleReminder => await ScheduleReminderAsync(action, automationEvent, cancellationToken).ConfigureAwait(false),
            AutomationActionType.UpdateField => await UpdateFieldAsync(action, automationEvent, cancellationToken).ConfigureAwait(false),
            _ => $"Action {action.ActionType} skipped (unsupported)"
        };

    private async Task<string> ApplyTagAsync(AutomationAction action, AutomationEvent automationEvent, CancellationToken cancellationToken)
    {
        var parameters = DeserializeParameters(action);
        var tag = GetRequiredString(parameters, "tag");
        var fieldName = parameters.TryGetValue("field", out var field) ? field?.ToString() : "Tags";

        if (!TryGetGuid(automationEvent.Payload, "tableId", out var tableId) ||
            !TryGetGuid(automationEvent.Payload, "recordId", out var recordId))
        {
            return "Tag skipped (missing record context)";
        }

        var dataEngine = _dataEngineFactory();

        var record = await dataEngine.GetAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return "Tag skipped (record not found)";
        }

        using var suppression = AutomationExecutionContext.Suppress();

        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson, SerializerOptions) ??
                   new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (data.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            data = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
        }

        var tags = ExtractTags(data, fieldName);
        if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            tags.Add(tag);
        }

        data[fieldName!] = tags;
        await dataEngine.UpdateAsync(tableId, recordId, data, cancellationToken).ConfigureAwait(false);

        return $"tag:{tag}";
    }

    private async Task<string> CreateNoteAsync(AutomationAction action, AutomationEvent automationEvent, CancellationToken cancellationToken)
    {
        var parameters = DeserializeParameters(action);
        var title = parameters.TryGetValue("title", out var titleValue) ? titleValue?.ToString() : automationEvent.Name;
        var content = parameters.TryGetValue("content", out var contentValue)
            ? contentValue?.ToString()
            : SerializePayload(automationEvent.Payload);

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Note title is required for CreateNote action.");
        }

        var links = BuildLinks(automationEvent);
        await _noteService.CreateTextNoteAsync(title!, content ?? string.Empty, links, cancellationToken).ConfigureAwait(false);
        return "note:created";
    }

    private async Task<string> ScheduleReminderAsync(AutomationAction action, AutomationEvent automationEvent, CancellationToken cancellationToken)
    {
        var parameters = DeserializeParameters(action);
        var title = GetRequiredString(parameters, "title");
        var startAt = GetDateTimeOffset(parameters, "start") ?? DateTimeOffset.UtcNow.AddHours(1);
        var reminderAt = GetDateTimeOffset(parameters, "reminderAt") ?? startAt.AddMinutes(-30);

        var evt = new S_Event
        {
            Title = title,
            Description = parameters.TryGetValue("description", out var description) ? description?.ToString() : automationEvent.Name,
            Start = startAt,
            End = GetDateTimeOffset(parameters, "end"),
            ReminderAt = reminderAt,
            IsCompleted = false,
            Links = BuildLinks(automationEvent).ToList()
        };

        await _agendaService.AddEventAsync(evt, cancellationToken).ConfigureAwait(false);
        return "reminder:scheduled";
    }

    private async Task<string> UpdateFieldAsync(AutomationAction action, AutomationEvent automationEvent, CancellationToken cancellationToken)
    {
        var parameters = DeserializeParameters(action);
        var fieldName = GetRequiredString(parameters, "field");
        var hasValue = parameters.TryGetValue("value", out var rawValue);
        if (!hasValue)
        {
            throw new InvalidOperationException("Parameter 'value' is required.");
        }

        if (!TryGetGuid(automationEvent.Payload, "tableId", out var tableId) ||
            !TryGetGuid(automationEvent.Payload, "recordId", out var recordId))
        {
            return "update skipped (missing record context)";
        }

        var dataEngine = _dataEngineFactory();
        var record = await dataEngine.GetAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return "update skipped (record not found)";
        }

        var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.DataJson, SerializerOptions) ??
                   new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (data.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            data = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
        }

        var normalizedValue = NormalizeParameterValue(rawValue);
        data.TryGetValue(fieldName, out var existing);

        if (AreEquivalent(existing, normalizedValue))
        {
            return $"field:{fieldName} unchanged";
        }

        data[fieldName] = normalizedValue;

        using var suppression = AutomationExecutionContext.Suppress();
        await dataEngine.UpdateAsync(tableId, recordId, data, cancellationToken).ConfigureAwait(false);
        return $"field:{fieldName} updated";
    }

    private static IReadOnlyCollection<S_Link> BuildLinks(AutomationEvent automationEvent)
    {
        var links = new List<S_Link>();
        if (TryGetGuid(automationEvent.Payload, "recordId", out var recordId) &&
            TryGetGuid(automationEvent.Payload, "tableId", out var tableId))
        {
            links.Add(new S_Link
            {
                SourceType = nameof(S_Event),
                SourceId = Guid.NewGuid(),
                TargetType = "Record",
                TargetId = recordId,
                Relation = tableId.ToString(),
                Type = "automation",
                CreatedBy = AuthorizationDefaults.DefaultUserId,
                Reason = "automation rule"
            });
        }

        return links;
    }

    private static List<string> ExtractTags(IDictionary<string, object?> data, string? fieldName)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return result;
        }

        if (data.TryGetValue(fieldName, out var existing) && existing is not null)
        {
            switch (existing)
            {
                case JsonElement element when element.ValueKind == JsonValueKind.Array:
                    result.AddRange(element.EnumerateArray().Select(e => e.GetString()).Where(e => !string.IsNullOrWhiteSpace(e))!);
                    break;
                case JsonElement element when element.ValueKind == JsonValueKind.String:
                    result.Add(element.GetString()!);
                    break;
                case IEnumerable<object?> enumerable:
                    result.AddRange(enumerable.Select(v => v?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s))!);
                    break;
                case string str:
                    result.AddRange(str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
            }
        }

        return result;
    }

    private static bool EvaluateConditions(S_AutomationRule rule, IReadOnlyDictionary<string, object?> payload)
    {
        foreach (var condition in rule.Conditions.OrderBy(c => c.Id))
        {
            var definition = ParseCondition(condition);
            if (!Evaluate(definition, payload))
            {
                return false;
            }
        }

        return true;
    }

    private static AutomationConditionDefinition ParseCondition(AutomationCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Expression))
        {
            return new AutomationConditionDefinition("payload", AutomationConditionOperator.Exists, null);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<AutomationConditionDefinition>(condition.Expression, SerializerOptions);
            if (parsed is not null)
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // Fallback to heuristic parsing below.
        }

        var parts = condition.Expression.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 3 && Enum.TryParse(parts[1], true, out AutomationConditionOperator op))
        {
            return new AutomationConditionDefinition(parts[0], op, parts[2]);
        }

        return new AutomationConditionDefinition(condition.Expression, AutomationConditionOperator.Exists, null);
    }

    private static bool Evaluate(AutomationConditionDefinition condition, IReadOnlyDictionary<string, object?> payload)
    {
        var left = ResolveValue(condition.Left, payload);
        var right = condition.Right;

        return condition.Operator switch
        {
            AutomationConditionOperator.Exists => left is not null,
            AutomationConditionOperator.NotExists => left is null,
            AutomationConditionOperator.Equals => string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            AutomationConditionOperator.NotEquals => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase),
            AutomationConditionOperator.Contains => left?.Contains(right ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            AutomationConditionOperator.StartsWith => left?.StartsWith(right ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            AutomationConditionOperator.EndsWith => left?.EndsWith(right ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            AutomationConditionOperator.GreaterThan => CompareNumbers(left, right) > 0,
            AutomationConditionOperator.LessThan => CompareNumbers(left, right) < 0,
            _ => false
        };
    }

    private static string? ResolveValue(string path, IReadOnlyDictionary<string, object?> payload)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!path.Contains('.', StringComparison.Ordinal))
        {
            return payload.TryGetValue(path, out var value) ? value?.ToString() : null;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        object? current = payload;

        foreach (var segment in segments)
        {
            if (current is IReadOnlyDictionary<string, object?> dict &&
                dict.TryGetValue(segment, out var value))
            {
                current = value;
                continue;
            }

            if (current is JsonElement element)
            {
                current = element.ValueKind switch
                {
                    JsonValueKind.Object when element.TryGetProperty(segment, out var child) => child,
                    _ => null
                };
                continue;
            }

            return null;
        }

        return current?.ToString();
    }

    private static int CompareNumbers(string? left, string? right)
    {
        if (double.TryParse(left, out var l) && double.TryParse(right, out var r))
        {
            return l.CompareTo(r);
        }

        return 0;
    }

    private async Task<List<S_AutomationRule>> LoadRulesAsync(AutomationEvent automationEvent, CancellationToken cancellationToken)
    {
        var query = _db.AutomationRules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .Where(r => r.IsEnabled && r.Trigger == automationEvent.Trigger && string.Equals(r.TriggerFilter, automationEvent.Name, StringComparison.OrdinalIgnoreCase));

        if (automationEvent.ModuleId.HasValue)
        {
            query = query.Where(r => r.ModuleId == automationEvent.ModuleId.Value);
        }

        return await query
            .OrderBy(r => r.Name)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, object?> DeserializeParameters(AutomationAction action)
    {
        if (string.IsNullOrWhiteSpace(action.ParametersJson))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(action.ParametersJson, SerializerOptions)
                ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?> { ["raw"] = action.ParametersJson };
        }
    }

    private static string SerializePayload(IReadOnlyDictionary<string, object?> payload)
        => JsonSerializer.Serialize(payload, SerializerOptions);

    private static bool TryGetGuid(IReadOnlyDictionary<string, object?> payload, string key, out Guid value)
    {
        if (payload.TryGetValue(key, out var candidate))
        {
            switch (candidate)
            {
                case Guid guid:
                    value = guid;
                    return true;
                case string str when Guid.TryParse(str, out var parsed):
                    value = parsed;
                    return true;
                case JsonElement element when element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var parsedElement):
                    value = parsedElement;
                    return true;
            }
        }

        value = Guid.Empty;
        return false;
    }

    private static string GetRequiredString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value?.ToString()))
        {
            throw new InvalidOperationException($"Parameter '{key}' is required.");
        }

        return value!.ToString()!;
    }

    private static DateTimeOffset? GetDateTimeOffset(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.ToString(), out var date) ? date : null;
    }

    private async Task<AutomationExecution?> FindExistingExecutionAsync(Guid ruleId, string payloadSnapshot, CancellationToken cancellationToken)
        => await _db.AutomationExecutions.AsNoTracking()
            .Where(e => e.RuleId == ruleId && e.PayloadSnapshot == payloadSnapshot &&
                        (e.Status == AutomationExecutionStatus.Succeeded || e.Status == AutomationExecutionStatus.Running))
            .OrderByDescending(e => e.StartedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private static object? NormalizeParameterValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => element.EnumerateArray().Select(e => NormalizeParameterValue(e)).ToList(),
                JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText(), SerializerOptions),
                _ => element.ToString()
            };
        }

        return value;
    }

    private static bool AreEquivalent(object? left, object? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        var normalizedLeft = NormalizeParameterValue(left);
        var normalizedRight = NormalizeParameterValue(right);

        return string.Equals(
            JsonSerializer.Serialize(normalizedLeft, SerializerOptions),
            JsonSerializer.Serialize(normalizedRight, SerializerOptions),
            StringComparison.Ordinal);
    }
}
