# Providers and persistence

Mem0Sharp standardizes entirely on **`Microsoft.Extensions.AI`** (`IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`) for all intelligence and embedding operations, while keeping the storage layer swappable across in-memory, SQLite, PostgreSQL/pgvector, and Qdrant.

## Available model and storage ecosystems

| Capability | Ecosystem Integrations |
| --- | --- |
| **Chat & Extraction** | Any `Microsoft.Extensions.AI.IChatClient` (OpenAI, Azure, [OllamaSharp](https://github.com/awaescher/OllamaSharp), Google Gemini, ONNX Runtime GenAI, Anthropic, Mistral) |
| **Text Embeddings** | Any `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` (OpenAI, OllamaSharp, ONNX embeddings, deterministic local) |
| **Image Embeddings** | Any `Microsoft.Extensions.AI.IEmbeddingGenerator<DataContent, Embedding<float>>` / `IImageEmbeddingGenerator` (LocalImageEmbeddingGenerator, ONNX vision models, CLIP) |
| **Vector storage** | In-memory and Qdrant in core; SQLite and PostgreSQL/pgvector in dedicated packages |
| **Reranking** | Any `IChatClient` (via `LlmReranker`), Cohere, ZeroEntropy, local cross-encoders |
| **Security & Governance** | `IAdmissionGate` (Prompt injection filter, scope authority, novelty gate) |
| **Anti-Drift Verifier** | `IConsolidationVerifier` (`LlmConsolidationVerifier`, `HeuristicConsolidationVerifier`) |
| **Trajectory Logging** | `ITrajectoryStore` (`InMemoryTrajectoryStore` for STONE deferred extraction) |

---

## 1. OpenAI / Azure OpenAI

Use the official `OpenAI` and `Microsoft.Extensions.AI.OpenAI` packages:

```csharp
using System.ClientModel;
using Mem0Sharp;
using Microsoft.Extensions.AI;
using OpenAI;

var openAiClient = new OpenAIClient(
    new ApiKeyCredential(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!));

var chatClient = openAiClient.GetChatClient("gpt-5.6-luna").AsIChatClient();
var embeddings = openAiClient.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();

var memory = new MemoryService(
    embeddings: embeddings,
    extractor: new LlmMemoryExtractor(chatClient));
```

---

## 2. Ollama (via OllamaSharp)

Use [OllamaSharp](https://github.com/awaescher/OllamaSharp), the official active library for Ollama in .NET:

```powershell
dotnet add package OllamaSharp
```

```csharp
using Mem0Sharp;
using Microsoft.Extensions.AI;
using OllamaSharp;

var endpoint = new Uri("http://localhost:11434/");
var ollama = new OllamaApiClient(endpoint, "llama3.2");

var memory = new MemoryService(
    embeddings: (IEmbeddingGenerator<string, Embedding<float>>)ollama,
    extractor: new LlmMemoryExtractor((IChatClient)ollama));
```

---

## 3. ONNX Runtime GenAI (Local On-Device Inference)

Run 100% private, on-device SLM extraction (Phi-3.5 / Phi-4 / Llama 3.2 ONNX) without daemons or cloud endpoints. See the official [Microsoft Agent Framework ONNX Guide](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/onnx):

```csharp
using Mem0Sharp;
using Microsoft.Extensions.AI;

// Wrap onnx model in an IChatClient and embedding model in an IEmbeddingGenerator
var memory = new MemoryService(
    embeddings: new LocalEmbeddingGenerator(384),
    extractor: new LlmMemoryExtractor(onnxChatClient));
```

---

## 4. PostgreSQL / pgvector Persistence

Install the provider package:

```powershell
dotnet add package Mem0Sharp.PostgreSQL
```

```csharp
await using var store = new PostgresMemoryStore(new PostgresMemoryStoreOptions
{
    ConnectionString = "Host=localhost;Database=mem0;Username=postgres;Password=postgres",
    EmbeddingDimensions = 1536,
    TableName = "agent_memories"
});
await store.InitializeAsync();

var memory = new MemoryService(store: store, embeddings: embeddings, extractor: extractor);
```

---

## 5. Point-in-Time State Rollback & Recovery (VMG)

All persistent stores support rollback to previous timestamps or historical mutation checkpoints:

```csharp
// Roll back memory state to yesterday
var rollbackResult = await memory.RollbackAsync(DateTimeOffset.UtcNow.AddDays(-1));
Console.WriteLine($"Restored {rollbackResult.RestoredCount} memories.");
```
