# Getting started

Mem0Sharp targets .NET Standard 2.0, .NET 8, .NET 9, and .NET 10 and exposes the `MemoryService` API for long-term application memory.

For a complete executable version of this guide, run the [getting started sample](../samples/GettingStarted/README.md).

## Install

Install the NuGet package in a compatible .NET Standard 2.0 or .NET 8-10 application:

```powershell
dotnet add package Mem0Sharp
```

The default in-memory path runs entirely in process. Install `Mem0Sharp.PostgreSQL`
or `Mem0Sharp.SQLite` only when the application needs those persistence providers;
they carry their own database dependencies.

Reference the project instead when developing against a local checkout:

```powershell
dotnet add .\src\YourApp\YourApp.csproj reference .\src\Mem0Sharp\Mem0Sharp.csproj
```

Optional provider packages:

```powershell
dotnet add package Mem0Sharp.PostgreSQL
dotnet add package Mem0Sharp.SQLite
```

The package includes the PostgreSQL integration. Build the library with:

```powershell
dotnet build .\src\Mem0Sharp\Mem0Sharp.csproj
```

## Create a service

The parameterless constructor is deliberately useful for tests and offline development. It selects:

- `InMemoryStore` for storage.
- `LocalEmbeddingGenerator` for deterministic lexical hashing embeddings.
- `BasicMemoryExtractor` for conversation messages.

```csharp
using Mem0Sharp;

var memory = new MemoryService();
```

## Save and search

A memory belongs to a user by default. Optional agent, run, scope, and metadata values can be supplied when saving it.

```csharp
var added = await memory.AddAsync(
    "I prefer dark mode and Vim keybindings",
    userId: "alice",
    metadata: new Dictionary<string, string>
    {
        ["source"] = "settings"
    });

var results = await memory.SearchAsync(
    "Which editor settings does Alice prefer?",
    new MemoryFilter(UserId: "alice"),
    topK: 5);

foreach (var result in results)
{
    Console.WriteLine($"{result.Score:F3}: {result.Memory.Text}");
}
```

`SearchResult.Score` is a cosine-similarity score. Results are ordered from the highest score to the lowest score. The in-memory fallback excludes results below `MemoryOptions.MinimumScore`.

Mem0Sharp stores the originating `MemoryBehavior` and optional `MemoryType` on
each record. Searches are factual by default and return only
`MemoryBehavior.Normal`; use `MemorySearchOptions { IncludeNonFactual = true }`
or set `Behavior` to retrieve associative, personal, or procedural memories.

The default local embeddings are intended for deterministic development and test workflows, not as a semantic-quality baseline. Use a model-backed embedding provider for production retrieval.

## Store conversation memories

Pass messages when the memory should be extracted from a conversation. The default extractor turns each non-empty message into a memory and stores its role in metadata.

```csharp
await memory.AddAsync(
[
    new Message("user", "I live in Berlin."),
    new Message("assistant", "I will remember that.")
],
userId: "alice",
scope: MemoryScope.User);
```

For model-backed fact extraction, use `LlmMemoryExtractor` with an OpenAI-compatible client as described in [Providers and persistence](providers-and-persistence.md).

## Multimodal and image memories

Mem0Sharp natively supports multimodal messages and image embeddings via `Microsoft.Extensions.AI`:

### 1. Extracting memories from images with Vision LLMs

Pass messages with images (as URLs, byte arrays, or `DataContent`) to extract factual memories using any multimodal LLM (OpenAI GPT-4.1 / GPT-4o, Anthropic Claude Sonnet 4, Google Gemini 2.5, or Ollama Qwen2.5-VL / Llama 3.2 Vision):

```csharp
var imageBytes = await File.ReadAllBytesAsync("receipt.png");
await memory.AddAsync(
[
    new Message("user", "Here is my receipt for reimbursement"),
    Message.FromImage(imageBytes, "image/png")
],
userId: "alice");
```

### 2. Direct image vector search

Configure an `IImageEmbeddingGenerator` (or `LocalImageEmbeddingGenerator` for local testing) to store and search image vectors directly:

```csharp
var memory = new MemoryService(
    embeddings: new LocalEmbeddingGenerator(384),
    imageEmbeddings: new LocalImageEmbeddingGenerator(384));

// Add image memory
await memory.AddAsync(imageBytes, "image/png", new MemoryAddOptions
{
    UserId = "alice",
    Prompt = "Receipt for conference travel"
});

// Search by image
var results = await memory.SearchAsync(imageBytes, "image/png", new MemorySearchOptions
{
    Filter = new MemoryFilter(UserId: "alice"),
    TopK = 3
});
```

## Choose a memory behavior

`MemoryAddOptions.Behavior` optionally changes how inferred memories are shaped. The default is `MemoryBehavior.Normal`, which preserves the existing durable-fact extraction behavior.

```csharp
var result = await memory.AddAsync(messages, new MemoryAddOptions
{
    UserId = "alice",
    AgentId = "mira",
    Behavior = MemoryBehavior.PersonalMemory,
    Prompt = "You are Mira, a thoughtful companion who notices emotional meaning."
});
```

The available behaviors are:

- `Normal` extracts neutral, durable facts as before.
- `Dreaming` consolidates themes, emotional patterns, and tentative associations.
- `RandomThoughts` records useful or surprising associations inspired by the conversation.
- `PersonalMemory` records what the agent noticed or concluded in first-person language; use `Prompt` to supply its personality or perspective.

These opt-in modes differ from conventional Mem0-style fact extraction by allowing reflective and agent-owned memories, not only neutral user facts. Prompts require uncertain associations to remain tentative rather than being stored as invented facts.

Behavior shaping requires `Infer = true` and an `IBehaviorAwareMemoryExtractor`; the built-in `LlmMemoryExtractor` implements it. `Infer = false` stores content verbatim regardless of the selected behavior. Third-party `IMemoryExtractor` implementations remain source-compatible and continue to work with `Normal`.

See the [memory behaviors sample](../samples/MemoryBehaviors/README.md) for a runnable comparison of every mode.

## Read, update, and delete

```csharp
var memories = await memory.GetAllAsync(new MemoryFilter(UserId: "alice"));
var id = memories[0].Id;

var current = await memory.GetAsync(id);
var updated = await memory.UpdateAsync(id, "I prefer dark mode and Vim keybindings");

await memory.DeleteAsync(id);
var removed = await memory.DeleteAllAsync(new MemoryFilter(UserId: "alice"));

var history = await memory.GetHistoryAsync(id);
foreach (var entry in history)
{
    Console.WriteLine($"{entry.Event}: {entry.OldMemory} -> {entry.NewMemory}");
}
```

`UpdateAsync` regenerates the embedding. `DeleteAllAsync` returns the number of deleted memories and applies the same filter fields as search and listing. Built-in stores record chronological `Add`, `Update`, and `Delete` history events, including events created by filtered bulk deletion.

## Configure defaults

```csharp
var memory = new MemoryService(
    options: new MemoryOptions
    {
        DefaultTopK = 10,
        MinimumScore = 0.15,
        MaxCandidateCount = 500
    });
```

`MaxCandidateCount` limits how many memories the non-vector fallback examines. Vector stores apply `topK` in the database.

## Expose local MCP tools

The [`McpServer` sample](../samples/McpServer/README.md) exposes nine local tools over stdio using the official `ModelContextProtocol` .NET SDK. It uses the same local `IMemoryService` without calling a hosted Mem0 service.

```csharp
dotnet run --project .\samples\McpServer\McpServer.csproj
```

The sample registers the memory tools with dependency injection and uses the SDK's stdio transport. Add `ModelContextProtocol` to an application-specific host when embedding the same tool pattern in another process.

## Next steps

- Run the [sample projects](../samples/README.md) for complete local, Ollama, and PostgreSQL workflows.
- Use [Providers and persistence](providers-and-persistence.md) for model-backed embeddings and PostgreSQL.
- Use [API reference](api-reference.md) for interfaces, filters, scopes, and custom implementations.
- Use [Python feature parity](mem0-python-parity.md) to check which Mem0 behaviors and providers are implemented.
