using System.Text.RegularExpressions;

namespace MagiCore;

public sealed class RuleBasedEntityExtractor : IEntityExtractor
{
    private static readonly Regex QuotedTextRegex = new("[\\\"']([^\\\"']{2,100})[\\\"']", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProperNameRegex = new(@"\b(?:[A-Z][\p{L}\p{M}'-]*)(?:\s+[A-Z][\p{L}\p{M}'-]*)*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<IReadOnlyList<ExtractedEntity>> ExtractAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entities = new Dictionary<string, ExtractedEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in QuotedTextRegex.Matches(text)) Add(match.Groups[1].Value, EntityType.Concept, entities);
        foreach (Match match in ProperNameRegex.Matches(text)) Add(match.Value, EntityType.Other, entities);
        return Task.FromResult<IReadOnlyList<ExtractedEntity>>(entities.Values.ToArray());
    }

    private static void Add(string text, EntityType type, IDictionary<string, ExtractedEntity> entities)
    {
        var normalized = text.Trim();
        if (normalized.Length > 1) entities.TryAdd(normalized, new ExtractedEntity(normalized, type));
    }

}