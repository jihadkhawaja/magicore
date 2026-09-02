using System.Globalization;
using System.Text.RegularExpressions;

namespace Mem0Sharp;

public sealed class DeterministicTemporalQueryInterpreter : ITemporalQueryInterpreter
{
    private static readonly Regex IsoDatePattern = new(@"\b(?<date>\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex YearPattern = new(@"\b(?<year>(?:19|20)\d{2})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<TemporalQueryInterpretation?> InterpretAsync(string query, DateTimeOffset referenceTime, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(query);
        cancellationToken.ThrowIfCancellationRequested();

        var dates = IsoDatePattern.Matches(query)
            .Cast<Match>()
            .Select(match => ParseDate(match.Groups["date"].Value, referenceTime.Offset))
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToArray();
        if (dates.Length > 0)
        {
            var start = dates.Min();
            var end = dates.Length > 1 ? EndOfDay(dates.Max()) : EndOfDay(start);
            return Task.FromResult<TemporalQueryInterpretation?>(new(new MemoryTimeRange(start, end), 1, string.Join(", ", dates.Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))));
        }

        var yearMatch = YearPattern.Match(query);
        if (yearMatch.Success && int.TryParse(yearMatch.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            var start = new DateTimeOffset(year, 1, 1, 0, 0, 0, referenceTime.Offset);
            return Task.FromResult<TemporalQueryInterpretation?>(new(new MemoryTimeRange(start, start.AddYears(1).AddTicks(-1)), 0.95, yearMatch.Value));
        }

        var normalized = query.ToLowerInvariant();
        var today = StartOfDay(referenceTime);
        TemporalQueryInterpretation? relative = normalized.Contains("yesterday", StringComparison.Ordinal)
            ? new(new MemoryTimeRange(today.AddDays(-1), today.AddTicks(-1)), 0.9, "yesterday")
            : normalized.Contains("today", StringComparison.Ordinal)
                ? new(new MemoryTimeRange(today, EndOfDay(today)), 0.9, "today")
                : normalized.Contains("last week", StringComparison.Ordinal)
                    ? new(new MemoryTimeRange(today.AddDays(-7), today.AddTicks(-1)), 0.85, "last week")
                    : normalized.Contains("last month", StringComparison.Ordinal)
                        ? PreviousMonth(today)
                        : normalized.Contains("last year", StringComparison.Ordinal)
                            ? PreviousYear(today)
                            : null;

        return Task.FromResult(relative);
    }

    private static DateTimeOffset? ParseDate(string value, TimeSpan offset) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date, offset)
            : null;

    private static DateTimeOffset StartOfDay(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset);

    private static DateTimeOffset EndOfDay(DateTimeOffset value) => StartOfDay(value).AddDays(1).AddTicks(-1);

    private static TemporalQueryInterpretation PreviousMonth(DateTimeOffset today)
    {
        var startOfMonth = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, today.Offset);
        var start = startOfMonth.AddMonths(-1);
        return new TemporalQueryInterpretation(new MemoryTimeRange(start, startOfMonth.AddTicks(-1)), 0.85, "last month");
    }

    private static TemporalQueryInterpretation PreviousYear(DateTimeOffset today)
    {
        var startOfYear = new DateTimeOffset(today.Year, 1, 1, 0, 0, 0, today.Offset);
        var start = startOfYear.AddYears(-1);
        return new TemporalQueryInterpretation(new MemoryTimeRange(start, startOfYear.AddTicks(-1)), 0.85, "last year");
    }
}