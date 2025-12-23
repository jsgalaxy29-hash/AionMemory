using System;
using System.Collections.Generic;

namespace Aion.Domain;

public enum AutomationConditionOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    LessThan,
    Exists,
    NotExists
}

public sealed record AutomationConditionDefinition(string Left, AutomationConditionOperator Operator, string? Right);

public sealed record AutomationActionDefinition(AutomationActionType ActionType, IReadOnlyDictionary<string, object?> Parameters);

public sealed record AutomationRuleDefinition(
    Guid Id,
    Guid ModuleId,
    string Name,
    AutomationTriggerType Trigger,
    string TriggerFilter,
    IReadOnlyList<AutomationConditionDefinition> Conditions,
    IReadOnlyList<AutomationActionDefinition> Actions,
    bool IsEnabled);

public sealed record AutomationEvent(
    string Name,
    AutomationTriggerType Trigger,
    IReadOnlyDictionary<string, object?> Payload,
    Guid? ModuleId = null,
    Guid? TableId = null);
