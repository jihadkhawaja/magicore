using System.ClientModel;
using System.Diagnostics;
using Mem0Sharp;
using Mem0Sharp.Evaluation;
using Microsoft.Extensions.AI;
using OpenAI;

var scenarioFilter = new List<string>();
var selfTest = false;
var listOnly = false;
var configPath = Path.Combine(AppContext.BaseDirectory, "evalconfig.local.yaml");
string? datasetPath = null;

for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--scenario" when index + 1 < args.Length:
            scenarioFilter.AddRange(args[++index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            break;
        case "--config" when index + 1 < args.Length:
            configPath = args[++index];
            break;
        case "--dataset" when index + 1 < args.Length:
            datasetPath = args[++index];
            break;
        case "--self-test":
            selfTest = true;
            break;
        case "--list":
            listOnly = true;
            break;
        case "--help" or "-h":
            Console.WriteLine("""
                Mem0Sharp LOCOMO Benchmark Evaluation Harness

                Usage:
                  dotnet run [options]

                Options:
                  --self-test           Run retrieval-only validation with deterministic local embeddings.
                  --scenario <names>    Run specific scenario(s) by name, comma-separated.
                  --config <path>       Path to evaluation YAML config (default: evalconfig.local.yaml).
                  --dataset <path>      Path to dataset JSON/JSONL (default: bundled snapshot).
                  --list                List registered scenarios and exit.
                """);
            return 0;
    }
}

if (listOnly)
{
    Console.WriteLine("Registered Evaluation Scenarios:");
    foreach (var scenario in Scenarios.All)
    {
        Console.WriteLine($"  - {scenario.Name,-28} {scenario.Description}");
    }
    return 0;
}

EvaluationDatasetSnapshot dataset;
try
{
    dataset = EvaluationDataset.LoadSnapshot(datasetPath);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Failed to load dataset: {exception.Message}");
    return 1;
}

var selected = scenarioFilter.Count == 0
    ? Scenarios.All
    : Scenarios.All.Where(s => scenarioFilter.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

if (selected.Count == 0)
{
    Console.Error.WriteLine($"No scenarios matched: {string.Join(", ", scenarioFilter)}");
    return 1;
}

if (selfTest)
{
    if (scenarioFilter.Count == 0)
    {
        selected = selected.Where(s => s.Infer == false).ToArray();
    }

    Console.WriteLine($"Self-test mode: deterministic local embeddings, retrieval metrics only, scenarios: {string.Join(", ", selected.Select(s => s.Name))}.");
}

EvaluationConfiguration configuration;
IChatClient? chatClient = null;
IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null;
LlmEvalHelper? llmHelper = null;

try
{
    if (selfTest)
    {
        configuration = File.Exists(configPath)
            ? EvaluationConfiguration.Load(configPath)
            : new EvaluationConfiguration();
    }
    else
    {
        configuration = EvaluationConfiguration.Load(configPath);
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(configuration.OpenAi.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(configuration.OpenAi.Endpoint) });

        chatClient = openAiClient.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient();
        embeddingGenerator = openAiClient.GetEmbeddingClient(configuration.OpenAi.EmbeddingModel).AsIEmbeddingGenerator();

        var judgeClient = string.Equals(configuration.OpenAi.JudgeModel, configuration.OpenAi.ChatModel, StringComparison.Ordinal)
            ? chatClient
            : openAiClient.GetChatClient(configuration.OpenAi.JudgeModel).AsIChatClient();

        llmHelper = new LlmEvalHelper(chatClient, judgeClient);
    }
}
catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

Console.WriteLine($"Running {selected.Count} scenario(s) against VectorData store...");
Console.WriteLine();

var reports = new List<ScenarioReport>();
foreach (var scenario in selected)
{
    Console.WriteLine($"[scenario] {scenario.Name}: {scenario.Description}");
    try
    {
        var runner = new ScenarioRunner(configuration, scenario, chatClient, embeddingGenerator, llmHelper, selfTest, dataset);
        var report = await runner.RunAsync(CancellationToken.None);
        reports.Add(report);
        var accuracy = report.Accuracy is null ? "n/a (retrieval-only)" : $"{report.Accuracy.Value:P0} ({report.Correct}/{report.Judged})";
        var f1 = report.MeanF1 is null ? "" : $" | F1: {report.MeanF1.Value:F2}";
        Console.WriteLine($"  memories: {report.MemoriesStored} | accuracy: {accuracy}{f1} | retrieval hit rate: {report.RetrievalHitRate:P0} | mean search: {report.MeanSearchLatencyMs:F0} ms");
    }
    catch (Exception exception)
    {
        reports.Add(new ScenarioReport
        {
            Name = scenario.Name,
            Description = scenario.Description,
            MemoriesStored = 0,
            IngestSeconds = 0,
            Questions = dataset.Questions.Count,
            Judged = 0,
            Correct = 0,
            RetrievalQuestions = 0,
            RetrievalHits = 0,
            RetrievalHitRate = 0,
            MeanSearchLatencyMs = 0,
            Error = exception.Message
        });
        Console.Error.WriteLine($"  [ERROR] {exception.Message}");
    }
}

var evaluationReport = new EvaluationReport
{
    Timestamp = DateTimeOffset.UtcNow,
    Mode = selfTest ? "self-test (deterministic local embeddings)" : "live (VectorData + model)",
    ChatModel = selfTest ? null : configuration.OpenAi.ChatModel,
    EmbeddingModel = selfTest ? null : configuration.OpenAi.EmbeddingModel,
    JudgeModel = selfTest ? null : configuration.OpenAi.JudgeModel,
    Store = "VectorData",
    Dataset = dataset.Name,
    ConversationCount = dataset.Conversations.Count,
    QuestionCount = dataset.Questions.Count,
    SyntheticDataset = true,
    ScenarioReports = reports
};

var outputPath = ReportWriter.Write(evaluationReport, configuration.Evaluation.ResultsDirectory);
Console.WriteLine();
Console.WriteLine($"Report written to {outputPath}");
return 0;
