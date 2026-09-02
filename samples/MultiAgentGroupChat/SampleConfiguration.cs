using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

internal sealed class SampleConfiguration
{
    public OpenAiSettings OpenAi { get; init; } = new();

    public static SampleConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Copy sampleconfig.example.yaml to sampleconfig.local.yaml and add your API key.",
                path);
        }

        using var reader = File.OpenText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        var configuration = deserializer.Deserialize<SampleConfiguration>(reader)
            ?? throw new InvalidDataException($"Configuration '{path}' is empty.");

        if (string.IsNullOrWhiteSpace(configuration.OpenAi.ApiKey)
            || configuration.OpenAi.ApiKey == "replace-with-an-openai-api-key")
        {
            throw new InvalidDataException("openAi.apiKey must be configured in sampleconfig.local.yaml.");
        }

        return configuration;
    }
}

internal sealed class OpenAiSettings
{
    public string Endpoint { get; init; } = "https://api.openai.com/";
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = "gpt-5.6-luna";
}