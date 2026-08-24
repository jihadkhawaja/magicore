# API reference

## Runtime requirements

`Mem0Sharp`, `Mem0Sharp.PostgreSQL`, and `Mem0Sharp.SQLite` target .NET Standard
2.0, .NET 8, .NET 9, and .NET 10. PostgreSQL and SQLite persistence are
distributed separately; install only the provider a deployment needs. The
PostgreSQL package uses the compatible Npgsql 7 line for .NET Standard 2.0 and
Npgsql 9 for modern .NET targets.

## MemoryService

`MemoryService` implements `IMemoryService` and is the main application entry point.

| Method | Purpose |
| --- | --- |
| `AddAsync(string, ...)` | Save one memory and generate its embedding. |
| `AddAsync(IEnumerable<Message>, ...)` | Extract and save memories from conversation messages (including multimodal messages with images). |
| `AddAsync(DataContent / ReadOnlyMemory<byte> / Uri, ...)` | Save an image memory, generate its image embedding, or extract facts via Vision LLM. |
| `AddManyAsync(IEnumerable<string>, ...)` | Deduplicate, batch embed, and save several memories. |
| `SearchAsync(string, ...)` | Return the most relevant memories for a text query. |
| `SearchAsync(DataContent / ReadOnlyMemory<byte> / Uri, ...)` | Return the most relevant memories using an image query vector. |
| `SearchManyAsync(IEnumerable<string>, ...)` | Search several queries with the same filter, using batch-capable embedding and vector providers when available. |
| `SearchManyAsync(IEnumerable<string>, MemorySearchOptions, ...)` | Search several queries with explicit behavior and retrieval policies. |
| `GetAsync(string)` | Retrieve one memory by ID. |
| `GetAllAsync(MemoryFilter?)` | List memories, newest updated first. |
| `GetPageAsync(MemoryPageOptions, MemoryFilter?)` | Return a page plus the total matching count. |
| `UpdateAsync(string, string, ...)` | Replace text and optionally metadata, then regenerate its embedding. |
| `DeleteAsync(string)` | Delete one memory by ID. |
| `DeleteAllAsync(MemoryFilter?)` | Delete all matching memories and return the count. |
| `GetHistoryAsync(string)` | Return chronological `ADD`, `UPDATE`, and `DELETE` events for one memory. |
| `GetRelationsAsync(string?)` | Return graph relations when a graph store is configured. |
| `ResetAsync()` | Clear memory, history, vector cache, entities, and graph state. |

All methods are asynchronous and accept an optional `CancellationToken`.

## Models

- `Memory` is the stored record. It contains `Id`, `Text`, `UserId`, optional `AgentId` and `RunId`, `Scope`, `Metadata`, `CreatedAt`, `UpdatedAt`, `Behavior`, and optional `MemoryType` provenance.
- `MemoryInput` is the extractor output used when creating memories.
- `Message` contains a conversation `Role`, `Content`, and optional `Contents` (`IReadOnlyList<AIContent>`) for multimodal messages (images/audio/data). Includes `Message.FromImage(...)` and `Message.FromTextAndImage(...)` factories.
- `SearchResult` contains a `Memory` and its similarity `Score`.
- `AddResult` contains the memories created by an add operation.
- `MemoryHistoryEntry` contains the event type, old and new text, memory ID, event ID, original creation time, event update time, deletion state, actor ID, and role.
- `MemoryAddOptions` controls identity, scope, inference, procedural memory, expiration, metadata, custom prompts, deduplication, and optional `MemoryBehavior` shaping.
- `MemoryBehavior` selects `Normal` (the unchanged default), `Dreaming`, `RandomThoughts`, or `PersonalMemory`. Non-normal modes require inference and an `IBehaviorAwareMemoryExtractor` such as `LlmMemoryExtractor`.
- `MemorySearchOptions` controls filtering, top K, threshold, hybrid scoring, explanations, reranking, explicit behavior selection, and `IncludeNonFactual`. Searches default to `MemoryBehavior.Normal`; associative and agent-owned memories require an explicit behavior or `IncludeNonFactual = true`.
- `MemoryUpdate` supports optional text, metadata, and expiration changes.
- `MemoryPage` contains paged results and total count.
- `SearchScoreDetails` separates semantic, keyword, entity/graph, and reranker signals.

## Filters and scopes

`MemoryFilter` can constrain reads, searches, and deletion by any combination of:

```csharp
var filter = new MemoryFilter(
    UserId: "alice",
    AgentId: "support-agent",
    RunId: "conversation-42",
    Scope: MemoryScope.Session);
```

`MemoryScope` has three values:

- `User` for facts associated with a user.
- `Session` for short-lived conversation or session context.
- `Agent` for facts associated with an agent.

The scope is metadata used for filtering; it does not automatically expire memories.

`MemoryFilter` can also constrain `Behavior` and `MemoryType`. Listing and
deletion include all behaviors unless those fields are supplied; search applies
the factual-only default described above.

## Tuning search

`MemoryOptions` controls the service defaults:

- `DefaultTopK` is used when a search does not provide `topK`.
- `MinimumScore` filters results when the service scans a non-vector store.
- `MaxCandidateCount` bounds that scan for non-vector stores.

A vector store such as `PostgresMemoryStore` applies similarity ordering and `topK` in the backend.

## Extension points

- `IEmbeddingGenerator` generates a vector for text.
- `IImageEmbeddingGenerator` generates a vector for image / multimodal `DataContent`.
- `OpenAiCompatibleClient`, `OllamaClient`, `LocalEmbeddingGenerator`, and `LocalImageEmbeddingGenerator` provide hosted and local embedding protocols.
- `OpenAiCompatibleClient`, `AnthropicClient`, and `OllamaClient` provide hosted and local chat protocols.
- `IMemoryExtractor` converts messages into `MemoryInput` values.
- `IBehaviorAwareMemoryExtractor` optionally adds behavior and persona-aware extraction without changing existing `IMemoryExtractor` implementations.
- `IMemoryStore` provides basic persistence operations.
- `IVectorMemoryStore` adds backend similarity search.
- `InMemoryStore` and `QdrantMemoryStore` are included in core. `SqliteMemoryStore` is provided by `Mem0Sharp.SQLite`; `PostgresMemoryStore`, `PostgresEntityStore`, and `PostgresGraphStore` are provided by `Mem0Sharp.PostgreSQL`.
- `IBulkMemoryStore` adds efficient filtered bulk deletion.
- `IMemoryHistoryStore` persists and retrieves the audit trail used by `GetHistoryAsync`.
- `IAtomicMemoryStore` atomically commits memory rows and their history events for add, update, and delete operations. The built-in in-memory, SQLite, and PostgreSQL stores implement it; custom stores can opt in when their backend supports transactions.
- `IBatchEmbeddingGenerator`, `IBatchMemoryStore`, and `IBatchVectorMemoryStore` enable batch pipelines. Batch vector stores can override `SearchBatchAsync`; the default implementation preserves compatibility with a sequential fallback.
- `IMemoryConflictResolver` produces structured memory actions.
- `IEntityExtractor`/`IEntityStore` and `IGraphMemoryExtractor`/`IGraphMemoryStore` provide relationship memory.
- `IMemoryReranker` reranks fused search candidates. Built-in implementations cover LLM scoring, Cohere, ZeroEntropy, and local cross-encoders through `ICrossEncoderScorer`.
- `IMemoryTelemetry` receives privacy-preserving operation events when configured.

`MemoryServiceConfiguration` composes these providers without any hosted Mem0 dependency. `SynchronousMemoryService` exposes blocking equivalents for applications that cannot use async APIs, including batch search, paging, and graph relation retrieval. The `samples/McpServer` project exposes local MCP tools through the official .NET SDK.

The service only requires `IMemoryStore`. If the supplied store does not implement `IVectorMemoryStore`, it falls back to generating and caching vectors in the service process. If it does not implement `IMemoryHistoryStore`, no history events are recorded and `GetHistoryAsync` returns an empty list. Relationship/entity stores are independent optional adapters; enrichment is prepared before memory writes and partial links are removed on failure, but a custom deployment that needs cross-table atomicity should provide a backend-specific aggregate store.

## Operational notes

- Use a stable `UserId` for each user so filters isolate data correctly.
- Keep embedding dimensions aligned between the configured provider and PostgreSQL.
- Treat `InMemoryStore` as ephemeral; all data is lost when the process exits.
- `OpenAiCompatibleClient` expects the provider root as `BaseAddress`, not the `/v1` path, because it appends `/v1/embeddings` and `/v1/chat/completions` itself.
