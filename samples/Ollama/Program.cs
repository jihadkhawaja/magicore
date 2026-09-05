using MagiCore;
using Microsoft.Extensions.AI;
using OllamaSharp;

var endpoint = new Uri("http://localhost:11434/");
var ollama = new OllamaApiClient(endpoint, "llama3.2");

var memory = new MemoryService(
    embeddings: (IEmbeddingGenerator<string, Embedding<float>>)ollama,
    extractor: new LlmMemoryExtractor((IChatClient)ollama));

var added = await memory.AddAsync(
[
    new Message("user", "My name is Alice and I prefer dark mode."),
    new Message("assistant", "I will remember that.")
],
new MemoryAddOptions { UserId = "alice" });

Console.WriteLine($"Extracted {added.Memories.Count} memories.");

var results = await memory.SearchAsync(
    "What display theme does Alice prefer?",
    new MemorySearchOptions
    {
        Filter = new MemoryFilter(UserId: "alice"),
        TopK = 3
    });

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}
