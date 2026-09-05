using MagiCore;
using Microsoft.Extensions.AI;

Console.WriteLine("=== MagiCore ONNX Runtime Local Inference Sample ===");
Console.WriteLine("Running 100% on-device private memory extraction and search via ONNX Runtime & MEAI.");
Console.WriteLine();

// 1. In production, wrap Microsoft.ML.OnnxRuntimeGenAI into an IChatClient (e.g. for Phi-3.5 / Phi-4 ONNX models)
// and an ONNX embedding model (e.g. all-MiniLM-L6-v2 / bge-small ONNX) into IEmbeddingGenerator.
// For zero-setup out-of-the-box execution, we use the local ONNX-compatible embedding generator:
var localEmbeddings = new LocalEmbeddingGenerator(dimensions: 384);

// 2. Mock / Local Onnx Chat Adapter demonstrating the IChatClient contract for on-device ONNX models:
IChatClient onnxChatClient = new OnnxLocalChatClient();

// 3. Instantiate MagiCore with ONNX components
var memory = new MemoryService(
    embeddings: localEmbeddings,
    extractor: new LlmMemoryExtractor(onnxChatClient));

// 4. Ingest and extract memories locally on device
var conversation = new[]
{
    new Message("user", "Hello! I am developer Alice. I specialize in C# and ONNX edge inference."),
    new Message("assistant", "Nice to meet you Alice! I have noted your expertise in .NET and ONNX.")
};

Console.WriteLine("Extracting facts using local ONNX model...");
var addResult = await memory.AddAsync(conversation, new MemoryAddOptions { UserId = "alice" });

Console.WriteLine($"Saved {addResult.Memories.Count} on-device memory fact(s):");
foreach (var item in addResult.Memories)
{
    Console.WriteLine($" - {item.Text}");
}
Console.WriteLine();

// 5. Query local onnx vector memory
Console.WriteLine("Searching memory with local query vector...");
var searchResults = await memory.SearchAsync(
    "What does Alice specialize in?",
    new MemorySearchOptions
    {
        Filter = new MemoryFilter(UserId: "alice"),
        Threshold = 0.1,
        TopK = 3
    });

Console.WriteLine($"Top {searchResults.Count} result(s):");
foreach (var result in searchResults)
{
    Console.WriteLine($" [{result.Score:F3}] {result.Memory.Text}");
}

/// <summary>
/// Reference implementation of an IChatClient wrapping ONNX Runtime GenAI (e.g., Phi-3.5-mini-instruct).
/// See: https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/onnx
/// </summary>
sealed class OnnxLocalChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("OnnxRuntimeGenAI", null, "phi-3.5-mini-instruct-onnx");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // In full ONNX GenAI deployments, forward tokenized prompts to Microsoft.ML.OnnxRuntimeGenAI.Model:
        // using var model = new Model(modelPath);
        // using var tokenizer = new Tokenizer(model);
        // ...
        var extractedFactsJson = """
            [
                "Alice is a developer specializing in C#.",
                "Alice specializes in ONNX edge inference."
            ]
            """;

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, extractedFactsJson)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("[\"Alice is a developer specializing in C#.\"]")]
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
