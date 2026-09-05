namespace MagiCore;

public sealed record MemoryFilter(
    string? UserId = null,
    string? AgentId = null,
    string? RunId = null,
    MemoryScope? Scope = null,
    FilterExpression? Metadata = null,
    bool IncludeExpired = false,
    MemoryBehavior? Behavior = null,
    string? MemoryType = null);

public enum FilterOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    In,
    NotIn,
    Contains,
    ContainsIgnoreCase,
    Exists
}

public enum FilterLogic
{
    And,
    Or,
    Not
}

public abstract record FilterExpression;

public sealed record MetadataFilter(string Key, FilterOperator Operator, object? Value = null) : FilterExpression;

public sealed record FilterGroup(FilterLogic Logic, IReadOnlyList<FilterExpression> Filters) : FilterExpression
{
    public FilterGroup(FilterLogic logic, params FilterExpression[] filters) : this(logic, (IReadOnlyList<FilterExpression>)filters) { }
}