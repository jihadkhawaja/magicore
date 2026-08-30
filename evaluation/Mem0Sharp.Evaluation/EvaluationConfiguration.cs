using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Mem0Sharp.Evaluation;

internal sealed class EvaluationConfiguration
{
    public EvalOpenAiSettings OpenAi { get; init; } = new();
    public EvalRunSettings Evaluation { get; init; } = new();

    public static EvaluationConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Copy evalconfig.example.yaml to evalconfig.local.yaml and add your API key.",
                path);
        }

        using var reader = File.OpenText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var configuration = deserializer.Deserialize<EvaluationConfiguration>(reader)
            ?? throw new InvalidDataException($"Configuration '{path}' is empty.");

        if (string.IsNullOrWhiteSpace(configuration.OpenAi.ApiKey)
            || configuration.OpenAi.ApiKey == "replace-with-an-openai-api-key")
        {
            throw new InvalidDataException("openAi.apiKey must be configured in evalconfig.local.yaml.");
        }

        return configuration;
    }
}

internal sealed class EvalOpenAiSettings
{
    public string Endpoint { get; init; } = "https://api.openai.com/";
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = "gpt-5.6-luna";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";
    public string JudgeModel { get; init; } = "gpt-5.6-luna";
}


internal sealed class EvalRunSettings
{
    public int TopK { get; init; } = 10;
    public int Concurrency { get; init; } = 4;
    public string ResultsDirectory { get; init; } = "results";
}
