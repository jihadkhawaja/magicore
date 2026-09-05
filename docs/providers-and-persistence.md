# Providers and persistence

MagiCore standardizes entirely on **`Microsoft.Extensions.AI`** (`IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>`) for all intelligence and embedding operations, and **`Microsoft.Extensions.VectorData`** (`VectorStore` and `VectorStoreCollection<TKey, TRecord>`) for universal vector database persistence.

## Available model and storage ecosystems

| Capability | Ecosystem Integrations |
| --- | --- |
| **Chat & Extraction** | Any `Microsoft.Extensions.AI.IChatClient` (OpenAI, Azure, [OllamaSharp](https://github.com/awaescher/OllamaSharp), Google Gemini, ONNX Runtime GenAI, Anthropic, Mistral) |
| **Text Embeddings** | Any `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` (OpenAI, OllamaSharp, ONNX embeddings, deterministic local) |
| **Image Embeddings** | Any `Microsoft.Extensions.AI.IEmbeddingGenerator<DataContent, Embedding<float>>` / `IImageEmbeddingGenerator` (LocalImageEmbeddingGenerator, ONNX vision models, CLIP) |
| **Vector storage** | In-memory and Qdrant in core; any vector database via `VectorDataMemoryStore` (Azure AI Search, PostgreSQL/pgvector, SQLite, Redis, Milvus, Pinecone, etc.) |
| **Reranking** | Any `IChatClient` (via `LlmReranker`), Cohere, ZeroEntropy, local cross-encoders |
| **Security & Governance** | `IAdmissionGate` (Prompt injection filter, scope authority, novelty gate) |
| **Anti-Drift Verifier** | `IConsolidationVerifier` (`LlmConsolidationVerifier`, `HeuristicConsolidationVerifier`) |
| **Trajectory Logging** | `ITrajectoryStore` (`InMemoryTrajectoryStore` for STONE deferred extraction) |

---

## 1. OpenAI / Azure OpenAI

Use the official `OpenAI` and `Microsoft.Extensions.AI.OpenAI` packages:

```csharp
using System.ClientModel;
using MagiCore;
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
using MagiCore;
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
using MagiCore;
using Microsoft.Extensions.AI;

// Wrap onnx model in an IChatClient and embedding model in an IEmbeddingGenerator
var memory = new MemoryService(
    embeddings: new LocalEmbeddingGenerator(384),
    extractor: new LlmMemoryExtractor(onnxChatClient));
```

---

## 4. Microsoft.Extensions.VectorData Persistence (PostgreSQL, SQLite, Azure AI Search, Redis, etc.)

`VectorDataMemoryStore` is included directly in `MagiCore`. Connect any `VectorStore` or `VectorStoreCollection` (e.g. from Semantic Kernel or CommunityToolkit.AI):

```csharp
using MagiCore;
using Microsoft.Extensions.VectorData;

// Example: Any VectorStore instance (Azure AI Search, Postgres/pgvector, SQLite, Redis, Qdrant, Milvus)
VectorStore vectorStore = GetVectorStore();

var store = new VectorDataMemoryStore(vectorStore, new VectorDataMemoryStoreOptions
{
    CollectionName = "agent_memories",
    VectorDimensions = 1536,
    AutoCreateCollection = true
});
await store.InitializeAsync();

var memory = new MemoryService(store: store, embeddings: embeddings, extractor: extractor);
```

Set `VectorDimensions` to the exact output size of the configured embedding generator before creating the collection.

---

## 5. Point-in-Time Reads and Rollback

`SearchAtAsync` and `GetAllAtAsync` reconstruct memory state at or before a timestamp without changing current memories.

```csharp
var pointInTime = DateTimeOffset.UtcNow;

// After later updates or deletions, search the historical state globally.
var global = await memory.SearchAtAsync(
    "tea",
    pointInTime,
    new MemorySearchOptions { TopK = 10, Threshold = 0 });

// Search the same historical state for one user and metadata subject.
var scoped = await memory.SearchAtAsync(
    "tea",
    pointInTime,
    new MemorySearchOptions
    {
        Filter = new MemoryFilter(
            UserId: "alice",
            Metadata: new MetadataFilter(
                "subject",
                FilterOperator.Equal,
                "beverages")),
        TopK = 5,
        Threshold = 0
    });
```

`RollbackAsync` is destructive. With a filter, it restores only historical snapshots that matched the filter at the requested timestamp. Matching memories created after that timestamp are deleted using their current state for filter evaluation; nonmatching memories remain unchanged.

Point-in-time reads require an `ITemporalMemoryStore`. `InMemoryStore` and `VectorDataMemoryStore` support them; `QdrantMemoryStore` does not. `VectorDataMemoryStore` currently reconstructs history from mutations captured by the active store instance, so point-in-time reads and rollback are not restart-safe even when a history collection is configured.
