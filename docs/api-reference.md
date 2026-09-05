# API reference

## Runtime requirements

`MagiCore` targets .NET Standard 2.0, .NET 8, .NET 9, and .NET 10. It includes `VectorDataMemoryStore` providing adapters for any `Microsoft.Extensions.VectorData` vector store connector (such as Azure AI Search, PostgreSQL/pgvector, SQLite, Redis, Qdrant, Milvus, Pinecone, etc.).

## MemoryService

`MemoryService` implements `IMemoryService` and is the main application entry point.

| Method | Purpose |
| --- | --- |
| `AddAsync(string, ...)` | Save one memory and generate its embedding. |
| `AddAsync(IEnumerable<Message>, ...)` | Extract and save memories from conversation messages (including multimodal messages with images). |
| `AddAsync(DataContent / ReadOnlyMemory<byte> / Uri, ...)` | Save an image memory, generate its image embedding, or extract facts via Vision LLM. |
| `AddManyAsync(IEnumerable<string>, ...)` | Deduplicate, batch embed, and save several memories. |
| `SearchAsync(string, ...)` | Return the most relevant memories for a text query. |
| `SearchAtAsync(string, DateTimeOffset, MemorySearchOptions?)` | Search reconstructed memory state at a timestamp without changing current memories. |
| `SearchAsync(DataContent / ReadOnlyMemory<byte> / Uri, ...)` | Return the most relevant memories using an image query vector. |
| `SearchManyAsync(IEnumerable<string>, ...)` | Search several queries with the same filter, using batch-capable embedding and vector providers when available. |
| `SearchManyAsync(IEnumerable<string>, MemorySearchOptions, ...)` | Search several queries with explicit behavior and retrieval policies. |
| `GetAsync(string)` | Retrieve one memory by ID. |
| `GetAllAsync(MemoryFilter?)` | List memories, newest updated first. |
| `GetAllAtAsync(DateTimeOffset, MemoryFilter?)` | List filtered memory state at a timestamp without changing current memories. |
| `GetPageAsync(MemoryPageOptions, MemoryFilter?)` | Return a page plus the total matching count. |
| `UpdateAsync(string, string, ...)` | Replace text and optionally metadata, then regenerate its embedding. |
| `DeleteAsync(string)` | Delete one memory by ID. |
| `DeleteAllAsync(MemoryFilter?)` | Delete all matching memories and return the count. |
| `GetHistoryAsync(string)` | Return chronological `ADD`, `UPDATE`, and `DELETE` events for one memory. |
| `RollbackAsync(DateTimeOffset, MemoryFilter?)` | Restore matching memories to their state at a timestamp and delete matching memories created later. |
| `RollbackToHistoryAsync(string)` | Roll back memory state to the timestamp of one history entry. |
| `GetRelationsAsync(string?)` | Return graph relations when a graph store is configured. |
| `ResetAsync()` | Clear memory, history, vector cache, entities, and graph state. |

All methods are asynchronous and accept an optional `CancellationToken`.

## Models

- `Memory` is the stored record. It contains `Id`, `Text`, `UserId`, optional `AgentId` and `RunId`, `Scope`, `Metadata`, `CreatedAt`, `UpdatedAt`, `Behavior`, and optional `MemoryType` provenance.
- `MemoryInput` is the extractor output used when creating memories.
- `Message` contains a conversation `Role`, `Content`, and optional `Contents` (`IReadOnlyList<AIContent>`) for multimodal messages (images/audio/data). Includes `Message.FromImage(...)` and `Message.FromTextAndImage(...)` factories.
- `SearchResult` contains a `Memory` and its similarity `Score`.
- `AddResult` contains the memories created by an add operation.
- `MemoryHistoryEntry` contains the event type, old and new text, a complete memory snapshot and embedding, memory ID, event ID, original creation time, event update time, deletion state, actor ID, and role.
- `MemoryAddOptions` controls identity, scope, inference, procedural memory, expiration, metadata, custom prompts, deduplication, optional `MemoryBehavior` shaping, and an optional event `ReferenceTime`.
- `MemoryBehavior` selects `Normal` (the unchanged default), `Dreaming`, `RandomThoughts`, or `PersonalMemory`. Non-normal modes require inference and an `IBehaviorAwareMemoryExtractor` such as `LlmMemoryExtractor`.
- `MemorySearchOptions` controls filtering, top K, threshold, hybrid scoring, explanations, reranking, explicit behavior selection, `IncludeNonFactual`, and opt-in event-time retrieval. Searches default to `MemoryBehavior.Normal`; associative and agent-owned memories require an explicit behavior or `IncludeNonFactual = true`.
- `MemoryUpdate` supports optional text, metadata, reference-time, and expiration changes. Set `UpdateReferenceTime = true` to replace or clear the event timestamp.
- `MemoryPage` contains paged results and total count.
- `SearchScoreDetails` separates semantic, keyword, entity/graph, and reranker signals.
- `RollbackResult` reports the numbers of restored and deleted memories and the affected memory IDs.
- `SpatialPoint` contains three finite coordinates and calculates Euclidean distance with `DistanceTo`.
- `SpatialObservation` describes one map-scoped observation with a position, description, observation time, optional observer position and entity ID, and confidence.
- `SpatialRecallOptions` defines the map, user, optional agent, center, radius, result limit, earliest observation time, minimum confidence, and optional entity ID.
- `SpatialRecallResult` contains the stored `Memory`, decoded `SpatialObservation`, and distance from the recall center.
- `RoboticsObservation` wraps an object observation with a stable event ID, sensor source, exact coordinate-frame revision, positional uncertainty, and explicit visibility.
- `RoboticsRecallOptions` adds event-time, freshness, confidence-decay, uncertainty, and relocation policies to `SpatialRecallOptions`.
- `SpatialObjectMemory` is a reconstructed object belief with its state, last-seen position, decayed confidence, distance, relocation count, and ordered evidence.
- `RobotActionEpisode` stores measured start/end positions, heading, action, timing, outcome, and controller feedback for one attempt.
- `RobotEpisodeRecallOptions` filters action attempts by spatial scope, coordinate frame, completion cutoff, optional action, and optional wrapped heading tolerance.

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

## Spatial memory

`RememberSpatialAsync` and `RecallSpatialAsync` are provider-neutral extensions on `IMemoryService`:

```csharp
Task<AddResult> RememberSpatialAsync(
    SpatialObservation observation,
    MemoryAddOptions options,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<SpatialRecallResult>> RecallSpatialAsync(
    SpatialRecallOptions options,
    CancellationToken cancellationToken = default);
```

`RememberSpatialAsync` stores the observation description verbatim with `MemoryType = "spatial_memory"`, versioned JSON metadata, and `ReferenceTime = ObservedAt`. Inference and text deduplication are disabled so separate observations are retained. The supplied `MemoryAddOptions` still controls user, agent, run, scope, expiration, and additional metadata.

`SpatialObservation.ObservedAt` defaults to the current UTC time and `Confidence` defaults to `1`. Positions must contain finite coordinates, map IDs and descriptions cannot be blank, and confidence must be between `0` and `1`.

`SpatialRecallOptions` defaults to `Radius = 10`, `TopK = 10`, and `MinimumConfidence = 0`. Recall loads current memories for the required user and optional agent, then filters by memory type, map, expiration, confidence, `ObservedAfter`, optional entity ID, and inclusive Euclidean radius. Results are ordered by nearest distance, newest observation, and memory ID. Invalid or malformed spatial metadata is skipped.

Spatial recall scans records returned by `GetAllAsync`; it is geometric filtering rather than vector similarity search or database-native spatial indexing. Use stable user and agent IDs to bound the candidate set. Units are application-defined but must be consistent within a map. See the [Godot spatial memory sample](../samples/3DSpatialMemoryGodot/README.md) for a complete 3D workflow.

### Robotics object evidence

The robotics extensions build auditable object beliefs on the same memory stores:

```csharp
Task<AddResult> RememberObjectAsync(
    RoboticsObservation evidence,
    MemoryAddOptions options,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<SpatialObjectMemory>> RecallObjectsAsync(
    RoboticsRecallOptions options,
    CancellationToken cancellationToken = default);
```

`RememberObjectAsync` requires a stable `ObservationId`, `SourceId`, exact `FrameId`, and a `SpatialObservation` with an externally assigned `EntityId`. Frame units are meters. `PositionUncertainty` is a conservative error radius, not a covariance estimate. `ObjectVisibility.Absent` must come from an explicit visibility or coverage check; detector silence alone is not absence.

`RecallObjectsAsync` replays evidence through `RoboticsRecallOptions.At`, resolves each entity's history, and only then applies radius filtering. This prevents a stale location from replacing a newer relocation. Positive confidence decays by `ConfidenceHalfLife`; `FreshFor`, `MaximumUncertainty`, visibility, and conflicting evidence determine the `SpatialBeliefState`. The result retains ordered evidence and reports whether fresh sensing is required through `NeedsObservation`.

`RoboticsMemoryExtensions.AssociateObject` provides conservative label-and-distance association only when exactly one candidate matches in the same frame. It is not visual re-identification. `GetObjectRelations` derives `Near` and Y-up `Above` point relations only between fresh, unambiguous beliefs in the same map and frame. These relations do not imply support, containment, collision-free paths, or other physical affordances.

### Robot action episodes

Controller-reported attempts can be persisted and recalled independently of object evidence:

```csharp
Task<AddResult> RememberRobotEpisodeAsync(
    RobotActionEpisode episode,
    MemoryAddOptions options,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<RobotActionEpisode>> RecallRobotEpisodesAsync(
    RobotEpisodeRecallOptions options,
    CancellationToken cancellationToken = default);
```

An episode requires a stable ID, map and frame, mission, executed action, finite measured poses, ordered timestamps, outcome, and controller feedback. Recall filters by user, optional agent, map, exact frame, start-position radius, completion time, optional entity/action, and optional heading. Heading differences wrap at $2\pi$ and use `HeadingToleranceRadians`, which defaults to `0.35`. Results are newest first and identical retry writes appear once.

`RobotActionOutcome.Completed` means only that the primitive completed; it does not prove mission success. Recalled outcomes and feedback are untrusted historical context, not safety clearance for future motion.

## Tuning search

`MemoryOptions` controls the service defaults:

- `DefaultTopK` is used when a search does not provide `topK`.
- `MinimumScore` filters results when the service scans a non-vector store.
- `MaxCandidateCount` bounds that scan for non-vector stores.

A vector store such as `VectorDataMemoryStore` applies similarity ordering and `topK` in the backend.

### Event-time retrieval

`SearchAtAsync` answers a transaction-time question: what did the store contain at a historical instant? Event-time retrieval answers a different question: which current memories describe events in a requested period?

Supply `ReferenceTime` when the event or source conversation occurred. MagiCore stores it as round-trip timestamp metadata under `TemporalMemoryMetadata.ReferenceTimeKey`, so existing persistence providers require no schema migration.

```csharp
await memory.AddAsync("The rollout moved to April 21.", new MemoryAddOptions
{
    UserId = "alice",
    ReferenceTime = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero)
});

var results = await memory.SearchAsync("What changed in 2025?", new MemorySearchOptions
{
    Filter = new MemoryFilter(UserId: "alice"),
    EnableTemporalSearch = true
});
```

The built-in `DeterministicTemporalQueryInterpreter` recognizes ISO dates, years, `today`, `yesterday`, `last week`, `last month`, and `last year`. Relative expressions use `MemorySearchOptions.ReferenceTime`, or the current time when omitted. Automatic interpretation is disabled by default and applies only when confidence meets `MinimumTemporalConfidence` (default `0.8`). Low-confidence or unrecognized queries fail open without temporal filtering.

Use `TimeRange` for an explicit range. `IncludeUndatedMemories` defaults to `true` to avoid dropping relevant legacy memories; set it to `false` when the result must contain only event-dated evidence. Applications can provide another `ITemporalQueryInterpreter` through `MemoryService` or `MemoryServiceConfiguration`.

## Extension points

- `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI` generates vectors for text.
- `IImageEmbeddingGenerator` generates a vector for image / multimodal `DataContent`.
- `LocalEmbeddingGenerator` and `LocalImageEmbeddingGenerator` provide deterministic local defaults. Hosted models are supplied through provider SDKs that implement `Microsoft.Extensions.AI` abstractions.
- `IChatClient` from `Microsoft.Extensions.AI` supplies model-backed extraction, conflict resolution, graph extraction, and reranking.
- `IMemoryExtractor` converts messages into `MemoryInput` values.
- `IBehaviorAwareMemoryExtractor` optionally adds behavior and persona-aware extraction without changing existing `IMemoryExtractor` implementations.
- `IMemoryStore` provides persistence, vector search, batch operations, history, rollback, and reset.
- `ITemporalMemoryStore` opts a store into reconstructed point-in-time reads through `GetAllAtAsync`.
- `ITemporalQueryInterpreter` optionally converts a query into a confident event-time range; `DeterministicTemporalQueryInterpreter` is the local default.
- `InMemoryStore` and `VectorDataMemoryStore` implement `ITemporalMemoryStore`; `QdrantMemoryStore` does not.
- `IMemoryConflictResolver` produces structured memory actions.
- `IEntityExtractor`/`IEntityStore` and `IGraphMemoryExtractor`/`IGraphMemoryStore` provide relationship memory.
- `IMemoryReranker` reranks fused search candidates. Built-in implementations cover LLM scoring, Cohere, ZeroEntropy, and local cross-encoders through `ICrossEncoderScorer`.
- `IMemoryTelemetry` receives privacy-preserving operation events when configured.

`MemoryServiceConfiguration` composes these providers without requiring a hosted memory service. `SynchronousMemoryService` exposes blocking equivalents for applications that cannot use async APIs, including batch search, paging, and graph relation retrieval. The `samples/McpServer` project exposes local MCP tools through the official .NET SDK.

The service requires `IMemoryStore`. Point-in-time reads additionally require `ITemporalMemoryStore`; `MemoryService.GetAllAtAsync` and `SearchAtAsync` throw `NotSupportedException` when the configured store does not implement it. Relationship and entity stores remain independent optional adapters.

## Operational notes

- Use a stable `UserId` for each user so filters isolate data correctly.
- Keep embedding dimensions aligned between the configured provider and the vector database collection.
- Treat `InMemoryStore` as ephemeral; all data is lost when the process exits.
- Configure hosted model endpoints and credentials through the selected `Microsoft.Extensions.AI` provider SDK; MagiCore does not own provider-specific HTTP clients.
