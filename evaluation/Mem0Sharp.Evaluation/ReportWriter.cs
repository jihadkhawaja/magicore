using System.Text;
using System.Text.Json;

namespace Mem0Sharp.Evaluation;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static string Write(EvaluationReport report, string resultsDirectory)
    {
        Directory.CreateDirectory(resultsDirectory);
        var stamp = report.Timestamp.ToString("yyyyMMdd-HHmmss");
        var jsonPath = Path.Combine(resultsDirectory, $"evaluation-{stamp}.json");
        var markdownPath = Path.Combine(resultsDirectory, $"evaluation-{stamp}.md");

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(markdownPath, ToMarkdown(report));
        return markdownPath;
    }

    internal static string ToMarkdown(EvaluationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Mem0Sharp evaluation results");
        builder.AppendLine();
        builder.AppendLine($"- Date: {report.Timestamp:yyyy-MM-dd HH:mm} UTC");
        builder.AppendLine($"- Mode: {report.Mode}");
        if (report.ChatModel is not null) builder.AppendLine($"- Chat model: {report.ChatModel}");
        if (report.EmbeddingModel is not null) builder.AppendLine($"- Embedding model: {report.EmbeddingModel}");
        if (report.JudgeModel is not null) builder.AppendLine($"- Judge model: {report.JudgeModel}");
        builder.AppendLine($"- Store: {report.Store}");
        builder.AppendLine($"- Dataset: {report.Dataset} ({report.ConversationCount} conversations, {report.QuestionCount} questions)");
        builder.AppendLine("- Confidence intervals: Wilson 95% question-level intervals; they do not measure provider or model variance.");
        builder.AppendLine();

        if (report.Capabilities is not null)
        {
            builder.AppendLine("## Feature capability evaluation");
            builder.AppendLine();
            builder.AppendLine($"**{report.Capabilities.Passed} passed, {report.Capabilities.Failed} failed, {report.Capabilities.Skipped} skipped**");
            builder.AppendLine();
            builder.AppendLine("| Feature | Category | Status | Duration (ms) | Evidence |");
            builder.AppendLine("| --- | --- | --- | ---: | --- |");
            foreach (var check in report.Capabilities.Checks)
            {
                var evidence = check.Evidence.Replace("|", "\\|").Replace("\n", " ");
                builder.AppendLine($"| {check.Feature} | {check.Category} | {check.Status} | {check.DurationMs:F1} | {evidence} |");
            }
            builder.AppendLine();
        }

        builder.AppendLine("## Scenario summary");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Accuracy (J) | Mean F1 | Mean BLEU-1 | Retrieval hit rate | Memories | Mean search (ms) | Ingest (s) |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var scenario in report.ScenarioReports)
        {
            var accuracy = scenario.Accuracy is null
                ? "n/a"
                : $"{scenario.Accuracy.Value:P0} ({scenario.Correct}/{scenario.Judged}; {FormatInterval(scenario.AccuracyLower95, scenario.AccuracyUpper95)})";
            var f1 = scenario.MeanF1 is null ? "n/a" : $"{scenario.MeanF1.Value:F2}";
            var bleu = scenario.MeanBleu1 is null ? "n/a" : $"{scenario.MeanBleu1.Value:F2}";
            var retrieval = $"{scenario.RetrievalHitRate:P0} ({scenario.RetrievalHits}/{scenario.RetrievalQuestions}; {FormatInterval(scenario.RetrievalHitRateLower95, scenario.RetrievalHitRateUpper95)})";
            builder.AppendLine($"| {scenario.Name} | {accuracy} | {f1} | {bleu} | {retrieval} | {scenario.MemoriesStored} | {scenario.MeanSearchLatencyMs:F0} | {scenario.IngestSeconds:F1} |");
        }
        builder.AppendLine();

        builder.AppendLine("## Accuracy by category");
        builder.AppendLine();
        var categories = report.ScenarioReports
            .SelectMany(scenario => scenario.Categories)
            .Select(category => category.Category)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        builder.Append("| Scenario");
        foreach (var category in categories) builder.Append($" | {category}");
        builder.AppendLine();
        builder.Append("| ---");
        foreach (var _ in categories) builder.Append(" | ---");
        builder.AppendLine();
        foreach (var scenario in report.ScenarioReports)
        {
            builder.Append($"| {scenario.Name}");
            foreach (var categoryName in categories)
            {
                var category = scenario.Categories.SingleOrDefault(item => item.Category == categoryName);
                builder.Append(category is null || category.Questions == 0 ? " | n/a" : $" | {category.Accuracy:P0}");
            }
            builder.AppendLine();
        }
        builder.AppendLine();

        foreach (var scenario in report.ScenarioReports)
        {
            builder.AppendLine($"## Scenario: {scenario.Name}");
            builder.AppendLine();
            builder.AppendLine(scenario.Description);
            builder.AppendLine();
            if (scenario.Error is not null)
            {
                builder.AppendLine($"**Failed:** {scenario.Error}");
                builder.AppendLine();
                continue;
            }
            builder.AppendLine("| Question | Category | Judgment | F1 | Generated answer |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var result in scenario.Results)
            {
                var judgment = result.Correct is null ? (result.RetrievalHit ? "hit" : "miss") : (result.Correct.Value ? "CORRECT" : "WRONG");
                var f1 = result.F1 is null ? "-" : result.F1.Value.ToString("F2");
                var answer = (result.GeneratedAnswer ?? "-").Replace("|", "\\|").Replace("\n", " ");
                builder.AppendLine($"| {result.QuestionId} | {result.Category} | {judgment} | {f1} | {answer} |");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatInterval(double? lower, double? upper) => lower is null || upper is null ? "n/a" : $"95% CI {lower.Value:P0}-{upper.Value:P0}";
}
