using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MagiCore.Tests;

internal sealed class OpenAiTestConfiguration
{
    public OpenAiTestSettings OpenAi { get; init; } = new();

    public static OpenAiTestConfiguration Load(string path)
    {
        using var reader = File.OpenText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<OpenAiTestConfiguration>(reader)
            ?? throw new InvalidDataException($"Test configuration '{path}' is empty.");
    }
}

internal sealed class OpenAiTestSettings
{
    public string Endpoint { get; init; } = "https://api.openai.com/";
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = "gpt-5.6-luna";
    public string EmbeddingModel { get; init; } = "text-embedding-3-small";

    public OpenAIClient CreateClient()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidDataException("openAi.apiKey must be configured.");

        return new OpenAIClient(
            new ApiKeyCredential(ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(Endpoint, UriKind.Absolute) });
    }
}