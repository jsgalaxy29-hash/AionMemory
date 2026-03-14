namespace Aion.Domain;

public enum QueryProjection
{
    List,
    Detail
}

public enum QueryFilterOperator
{
    Equals,
    Contains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed record QueryFilter(string Field, QueryFilterOperator Operator, object? Value);

public sealed class QuerySpec
{
    public IList<QueryFilter> Filters { get; init; } = new List<QueryFilter>();

    public string? FullText { get; init; }

    public string? OrderBy { get; init; }

    public bool Descending { get; init; }

    public int? Skip { get; init; }

    public int? Take { get; init; }

    public QueryProjection Projection { get; init; } = QueryProjection.List;

    public string? View { get; init; }
}
