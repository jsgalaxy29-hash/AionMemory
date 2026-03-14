using System.Collections.Generic;

namespace Aion.AppHost.Services;

public sealed record RecordPage<T>(IReadOnlyList<T> Items, int TotalCount);
