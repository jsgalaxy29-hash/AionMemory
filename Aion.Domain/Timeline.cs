using System;
using System.Collections.Generic;

namespace Aion.Domain;

public sealed record TimelineQuery(
    int Take,
    int Skip = 0,
    Guid? ModuleId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null)
{
    public const int MaxPageSize = 200;

    public int NormalizedTake => Math.Min(Math.Max(1, Take), MaxPageSize);
    public int NormalizedSkip => Math.Max(0, Skip);
}

public sealed record TimelinePage(
    IReadOnlyCollection<S_HistoryEvent> Items,
    bool HasMore,
    int NextSkip);
