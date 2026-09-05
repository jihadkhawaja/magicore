namespace MagiCore;

internal static class HybridSearchScorer
{
    public static IReadOnlyList<SearchResult> ScoreAndRank(string query, IReadOnlyList<SearchResult> semanticResults, IReadOnlyDictionary<string, double> entityBoosts, double threshold, int topK, bool explain)
    {
        if (semanticResults.Count == 0) return [];

        var queryTerms = Tokenize(query);
        var keywordScores = queryTerms.Length == 0 ? new Dictionary<string, double>(StringComparer.Ordinal) : ScoreBm25(queryTerms, semanticResults);
        var hasKeyword = keywordScores.Count > 0;
        var hasEntity = entityBoosts.Count > 0;
        var maxPossible = 1d + (hasKeyword ? 1d : 0d) + (hasEntity ? 0.5d : 0d);

        return semanticResults.Select(result =>
            {
                var keyword = keywordScores.GetValueOrDefault(result.Memory.Id);
                var entity = entityBoosts.GetValueOrDefault(result.Memory.Id);
                var raw = result.Score + keyword + entity;
                var score = Math.Min(raw / maxPossible, 1);
                var details = explain
                    ? new SearchScoreDetails(result.Score, keyword, entity, Raw: raw, MaxPossible: maxPossible, Threshold: threshold)
                    : null;
                return new SearchResult(result.Memory, score, details);
            })
            .Where(result => result.Score >= threshold)
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToArray();
    }

    private static Dictionary<string, double> ScoreBm25(IReadOnlyList<string> queryTerms, IReadOnlyList<SearchResult> candidates)
    {
        const double k1 = 1.5;
        const double b = 0.75;
        var distinctTerms = queryTerms.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctTerms.Length == 0 || candidates.Count == 0) return new Dictionary<string, double>(StringComparer.Ordinal);

        var documents = new string[candidates.Count][];
        var totalLength = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            documents[i] = Tokenize(candidates[i].Memory.Text);
            totalLength += Math.Max(documents[i].Length, 1);
        }
        var averageLength = (double)totalLength / candidates.Count;

        var docFrequencies = new Dictionary<string, int>(distinctTerms.Length, StringComparer.Ordinal);
        foreach (var term in distinctTerms)
        {
            var df = 0;
            for (var i = 0; i < documents.Length; i++)
            {
                if (Array.IndexOf(documents[i], term) >= 0) df++;
            }
            docFrequencies[term] = df;
        }

        var rawScores = new Dictionary<string, double>(candidates.Count, StringComparer.Ordinal);
        for (var documentIndex = 0; documentIndex < documents.Length; documentIndex++)
        {
            var document = documents[documentIndex];
            if (document.Length == 0) continue;

            double score = 0;
            foreach (var term in distinctTerms)
            {
                var df = docFrequencies[term];
                if (df == 0) continue;

                var frequency = 0;
                for (var i = 0; i < document.Length; i++)
                {
                    if (string.Equals(document[i], term, StringComparison.Ordinal)) frequency++;
                }
                if (frequency == 0) continue;

                var inverseDocumentFrequency = Math.Log(1 + (documents.Length - df + 0.5) / (df + 0.5));
                score += inverseDocumentFrequency * frequency * (k1 + 1) /
                    (frequency + k1 * (1 - b + b * document.Length / averageLength));
            }

            if (score > 0) rawScores[candidates[documentIndex].Memory.Id] = NormalizeBm25(score, queryTerms.Count);
        }
        return rawScores;
    }

    private static double NormalizeBm25(double score, int queryTermCount)
    {
        var (midpoint, steepness) = queryTermCount switch
        {
            <= 3 => (5d, 0.7d),
            <= 6 => (7d, 0.6d),
            <= 9 => (9d, 0.5d),
            <= 15 => (10d, 0.5d),
            _ => (12d, 0.5d)
        };
        return 1 / (1 + Math.Exp(-steepness * (score - midpoint)));
    }

    private static string[] Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var span = text.AsSpan();
        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i < span.Length; i++)
        {
            if (char.IsLetterOrDigit(span[i]))
            {
                if (start < 0) start = i;
            }
            else if (start >= 0)
            {
                tokens.Add(span.Slice(start, i - start).ToString().ToLowerInvariant());
                start = -1;
            }
        }
        if (start >= 0)
        {
            tokens.Add(span.Slice(start).ToString().ToLowerInvariant());
        }
        return tokens.ToArray();
    }
}