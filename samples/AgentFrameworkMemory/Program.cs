using Mem0Sharp;
using Mem0Sharp.VectorData;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set the OPENAI_API_KEY environment variable before running this sample.");
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5.6-luna";

var vectorMemoryStore = VectorDataMemoryStore.CreateInMemory(new VectorDataMemoryStoreOptions
{
    CollectionName = "agent_memories"
});
await vectorMemoryStore.InitializeAsync();

IMemoryService memory = new MemoryService(store: vectorMemoryStore);

var agent = new ChatClientAgent(
    new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient(),
    new ChatClientAgentOptions
    {
        ChatOptions = new ChatOptions
        {
            Instructions = "You are a helpful assistant. Use remembered preferences when they are relevant, and do not invent memories."
        },
        AIContextProviders = [new Mem0ContextProvider(memory, userId: "alice")]
    });

var session = await agent.CreateSessionAsync();
Console.WriteLine("Tell the agent something it should remember, then ask about it later. Type 'exit' to stop.\n");

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;
    if (string.IsNullOrWhiteSpace(input))
        continue;

    var response = await agent.RunAsync(input, session);
    Console.WriteLine($"Agent: {response}\n");
}

/// <summary>
/// Bridges Microsoft Agent Framework's <see cref="AIContextProvider"/> with <see cref="MemoryService"/>.
/// Automatically injects remembered context before invocation and stores turns after invocation.
/// </summary>
internal sealed class Mem0ContextProvider(IMemoryService memory, string userId) : AIContextProvider
{
    protected override async ValueTask StoreAIContextAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var messages = context.RequestMessages.Concat(context.ResponseMessages ?? []);
        var text = string.Join(Environment.NewLine, messages.Select(message => $"{message.Role}: {message.Text}"));

        if (!string.IsNullOrWhiteSpace(text))
        {
            await memory.AddAsync(text, new MemoryAddOptions
            {
                UserId = userId,
                Infer = false
            }, cancellationToken: cancellationToken);
        }
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var memories = await memory.GetAllAsync(
            new MemoryFilter(UserId: userId),
            cancellationToken: cancellationToken);

        if (memories.Count == 0)
            return new AIContext();

        var remembered = string.Join(
            Environment.NewLine,
            memories
                .OrderByDescending(item => item.UpdatedAt)
                .Take(5)
                .Select(item => $"- {item.Text}"));

        return new AIContext
        {
            Instructions = $"Relevant memories for this user:\n{remembered}"
        };
    }
}
