using MagiCore;
using Microsoft.Extensions.AI;
using OpenAI;
using Xunit;

namespace MagiCore.Tests;

public sealed class OpenAiIntegrationTests
{
    private const string RunLiveTestsVariable = "MAGICORE_RUN_OPENAI_TESTS";

    [Fact]
    public void ExampleConfigurationCanCreateChatAndEmbeddingClient()
    {
        var configuration = OpenAiTestConfiguration.Load(ConfigurationPath("testsettings.example.yaml"));

        var client = configuration.OpenAi.CreateClient();
        var chatClient = client.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient();
        var embeddingGenerator = client.GetEmbeddingClient(configuration.OpenAi.EmbeddingModel).AsIEmbeddingGenerator();

        Assert.IsAssignableFrom<IChatClient>(chatClient);
        Assert.IsAssignableFrom<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalConfigurationCallsOpenAiChatAndEmbeddings()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunLiveTestsVariable), "1", StringComparison.Ordinal))
            return;

        var configuration = OpenAiTestConfiguration.Load(ConfigurationPath("testsettings.local.yaml"));
        var client = configuration.OpenAi.CreateClient();
        var chatClient = client.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient();
        var embeddingGenerator = client.GetEmbeddingClient(configuration.OpenAi.EmbeddingModel).AsIEmbeddingGenerator();

        var completion = await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Reply with exactly: magicore")]);
        var embeddings = await embeddingGenerator.GenerateAsync(["magicore integration test"]);

        Assert.Contains("magicore", completion.Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(embeddings);
    }

    private static string ConfigurationPath(string fileName) => Path.Combine(AppContext.BaseDirectory, fileName);
}