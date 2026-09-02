# Architecture

Mem0Sharp uses a pragmatic ports-and-adapters architecture across a dependency-free core package and optional provider packages. The public `Mem0Sharp` namespace remains stable, while the source tree separates domain types, contracts, application orchestration, and replaceable infrastructure.

Keeping one assembly preserves compatibility for existing consumers. The folders express ownership and dependency direction without requiring applications to reference several packages for the default experience.

## Dependency direction

```mermaid
flowchart LR
    Consumers[Applications and agents] --> Application[Application orchestration]
    Consumers --> Contracts[Contracts]
    Application --> Contracts
    Application --> Domain[Domain models]
    Infrastructure[Infrastructure adapters] --> Contracts
    Infrastructure --> Domain
    Intelligence[Memory intelligence] --> Contracts
    Intelligence --> Domain
    Telemetry[Telemetry decorators] --> Contracts
```

Dependencies point toward contracts and domain models. Contracts never depend on application services or concrete infrastructure. `MemoryService` coordinates use cases through interfaces and does not contain provider-specific HTTP or database logic.

## Source layout

| Folder | Responsibility |
| --- | --- |
| `Domain` | Memory, filtering, search, relationship, history, and operation models. |
| `Contracts` | Storage, embedding, intelligence, telemetry, relationship, and service ports. |
| `Application` | Use-case orchestration, composition, and internal search policies. |
| `Infrastructure/InMemory` | Ephemeral store adapters used by the default service and tests. |
| `Infrastructure/Extraction` | Deterministic built-in extraction implementations. |
| `Infrastructure/Embeddings` | Deterministic local embedding implementations. |
| `Infrastructure/OpenAI` | OpenAI-compatible HTTP adapters. |
| `Intelligence` | Provider-neutral LLM extraction, conflict resolution, procedural memory, graph extraction, and reranking policies. |
| `Telemetry` | Telemetry decorators and collectors. |
| `Facades` | Alternative API façades, including the synchronous wrapper. |
| `Infrastructure/VectorData` | Standard `Microsoft.Extensions.VectorData` persistence provider for any MEVD vector database. |

All public types remain in `namespace Mem0Sharp`. Folder names are architectural boundaries, not namespace segments.

## Composition

`MemoryService` is the application service. It accepts ports such as `IMemoryStore`, `IEmbeddingGenerator`, and `IMemoryExtractor` through constructor injection. `MemoryServiceConfiguration` is the explicit composition root when optional capabilities such as telemetry, graph memory, conflict resolution, or reranking are enabled.

The parameterless `MemoryService` path composes deterministic in-memory defaults for local development. Production applications should compose persistent stores and model providers at their own startup boundary.

Event-time retrieval is an additive projection over ordinary memory metadata. Ingestion records an optional canonical reference timestamp without replacing the memory's transaction timestamps. Search can apply an explicit range or use `ITemporalQueryInterpreter`; automatic interpretation is opt-in and fails open below its confidence threshold. This keeps persistence provider-neutral and distinct from point-in-time state reconstruction through `ITemporalMemoryStore`.

## Consistency boundaries

Built-in in-memory and VectorData stores implement `IAtomicMemoryStore`.
When available, `MemoryService` commits a memory row and its `ADD`, `UPDATE`, or
`DELETE` history event in the same transaction, including filtered bulk
deletes. Custom stores retain the basic `IMemoryStore` contract and can opt into
the stronger boundary when their backend supports it.

Entity and graph stores remain independent optional adapters because they may be
hosted in a different backend. Enrichment is extracted before a memory write;
link application is idempotent and removes partial links when an adapter fails.
Deletion removes links before deleting the memory, so a cleanup failure leaves
the memory available rather than creating references to missing data. A
deployment requiring cross-database atomicity should compose a backend-specific
aggregate store instead of relying on distributed transactions.

## Extension rules

1. Add provider-neutral data to `Domain` and provider-neutral behavior contracts to `Contracts`.
2. Keep orchestration in `Application`; application code may depend on contracts but not concrete adapters.
3. Put HTTP and vendor SDK code under core `Infrastructure`; put database adapters in their dedicated provider projects.
4. Put model-driven memory policies under `Intelligence` when they depend only on provider-neutral contracts.
5. Put protocol concerns under `Transports` and cross-cutting decorators under their dedicated folder.
6. Preserve the public `Mem0Sharp` namespace unless a planned major version explicitly introduces namespace migration.

Vector database integrations are standardized via `Microsoft.Extensions.VectorData.Abstractions` inside core `Mem0Sharp`. MCP hosting is kept in `samples/McpServer` and uses the official `ModelContextProtocol` SDK, leaving the core package independent of protocol hosting dependencies.