using System.Collections;
using System.Globalization;

namespace MagiCore;

internal static class MemoryFilterEvaluator
{
    public static bool Matches(Memory memory, MemoryFilter? filter, DateTimeOffset? now = null)
    {
        if (filter is null) return memory.ExpiresAt is null || memory.ExpiresAt > (now ?? DateTimeOffset.UtcNow);
        if (!filter.IncludeExpired && memory.ExpiresAt is not null && memory.ExpiresAt <= (now ?? DateTimeOffset.UtcNow)) return false;
        return (filter.UserId is null || memory.UserId == filter.UserId) &&
            (filter.AgentId is null || memory.AgentId == filter.AgentId) &&
            (filter.RunId is null || memory.RunId == filter.RunId) &&
            (filter.Scope is null || memory.Scope == filter.Scope) &&
            (filter.Behavior is null || memory.Behavior == filter.Behavior) &&
            (filter.MemoryType is null || memory.MemoryType == filter.MemoryType) &&
            (filter.Metadata is null || MatchesExpression(memory.Metadata, filter.Metadata));
    }

    private static bool MatchesExpression(IReadOnlyDictionary<string, string> metadata, FilterExpression expression) => expression switch
    {
        MetadataFilter condition => MatchesCondition(metadata, condition),
        FilterGroup { Logic: FilterLogic.And } group => group.Filters.All(item => MatchesExpression(metadata, item)),
        FilterGroup { Logic: FilterLogic.Or } group => group.Filters.Any(item => MatchesExpression(metadata, item)),
        FilterGroup { Logic: FilterLogic.Not } group => group.Filters.Count == 1
            ? !MatchesExpression(metadata, group.Filters[0])
            : throw new ArgumentException("A Not filter group must contain exactly one expression."),
        _ => throw new ArgumentOutOfRangeException(nameof(expression))
    };

    private static bool MatchesCondition(IReadOnlyDictionary<string, string> metadata, MetadataFilter condition)
    {
        Guard.NotNullOrWhiteSpace(condition.Key);
        var exists = metadata.TryGetValue(condition.Key, out var actual);
        if (condition.Operator == FilterOperator.Exists) return exists == Convert.ToBoolean(condition.Value ?? true, CultureInfo.InvariantCulture);
        if (!exists) return false;

        return condition.Operator switch
        {
            FilterOperator.Equal => Compare(actual!, condition.Value) == 0,
            FilterOperator.NotEqual => Compare(actual!, condition.Value) != 0,
            FilterOperator.GreaterThan => Compare(actual!, condition.Value) > 0,
            FilterOperator.GreaterThanOrEqual => Compare(actual!, condition.Value) >= 0,
            FilterOperator.LessThan => Compare(actual!, condition.Value) < 0,
            FilterOperator.LessThanOrEqual => Compare(actual!, condition.Value) <= 0,
            FilterOperator.In => Values(condition.Value).Any(value => Compare(actual!, value) == 0),
            FilterOperator.NotIn => Values(condition.Value).All(value => Compare(actual!, value) != 0),
            FilterOperator.Contains => actual!.Contains(Convert.ToString(condition.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.Ordinal),
            FilterOperator.ContainsIgnoreCase => actual!.Contains(Convert.ToString(condition.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(nameof(condition.Operator))
        };
    }

    private static int Compare(string actual, object? expected)
    {
        var expectedText = Convert.ToString(expected, CultureInfo.InvariantCulture) ?? string.Empty;
        if (decimal.TryParse(actual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumber) &&
            decimal.TryParse(expectedText, NumberStyles.Number, CultureInfo.InvariantCulture, out var expectedNumber))
        {
            return actualNumber.CompareTo(expectedNumber);
        }
        if (DateTimeOffset.TryParse(actual, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var actualDate) &&
            DateTimeOffset.TryParse(expectedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expectedDate))
        {
            return actualDate.CompareTo(expectedDate);
        }
        return string.Compare(actual, expectedText, StringComparison.Ordinal);
    }

    private static IEnumerable<object?> Values(object? value)
    {
        if (value is string or null) return [value];
        return value is IEnumerable values ? values.Cast<object?>() : [value];
    }
}