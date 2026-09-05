using System.ClientModel;
using MagiCore;
using Microsoft.Extensions.AI;
using OpenAI;

var configuration = SampleConfiguration.Load(
    Path.Combine(AppContext.BaseDirectory, "sampleconfig.local.yaml"));

var openAiClient = new OpenAIClient(
    new ApiKeyCredential(configuration.OpenAi.ApiKey),
    new OpenAIClientOptions { Endpoint = new Uri(configuration.OpenAi.Endpoint) });

var chatClient = openAiClient.GetChatClient(configuration.OpenAi.ChatModel).AsIChatClient();
var embeddingGenerator = openAiClient.GetEmbeddingClient(configuration.OpenAi.EmbeddingModel).AsIEmbeddingGenerator();

var memory = new MemoryService(
    embeddings: embeddingGenerator,
    extractor: new LlmMemoryExtractor(chatClient));

Message[] conversation =
[
    new("user", "The smell of rain reminds me of studying astronomy with my grandfather. I still look for Orion when I need courage."),
    new("assistant", "That sounds like both a comforting memory and a personal ritual.")
];

MemoryBehavior[] behaviors =
[
    MemoryBehavior.Normal,
    MemoryBehavior.Dreaming,
    MemoryBehavior.RandomThoughts,
    MemoryBehavior.PersonalMemory
];

foreach (var behavior in behaviors)
{
    var result = await memory.AddAsync(conversation, new MemoryAddOptions
    {
        UserId = "alice",
        AgentId = "mira",
        RunId = $"behavior-{behavior}",
        Behavior = behavior,
        Prompt = behavior == MemoryBehavior.PersonalMemory
            ? "You are Mira, a thoughtful companion who notices emotional meaning and speaks with gentle curiosity."
            : null
    });

    Console.WriteLine($"{behavior}:");
    foreach (var item in result.Memories)
    {
        Console.WriteLine($"  - {item.Text}");
    }
    Console.WriteLine();
}