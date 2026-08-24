# mem0-python parity

Mem0Sharp is an independent, standalone implementation of Mem0's open-source behavior for .NET. It does not call the hosted Mem0 Platform API and does not depend on mem0.ai at runtime. This ledger tracks behavioral parity with the self-hosted Python SDK. A row is complete only when its public API, persistence semantics, tests, and documentation are present.

Status meanings:

- **Complete**: supported by the built-in local and PostgreSQL paths and covered by tests.
- **Partial**: a useful subset exists, but Python behavior or provider breadth is missing.
- **Missing**: no supported public implementation exists yet.

## Core memory API

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| Add raw text | Complete | `AddAsync(string)` |
| Add conversation messages | Complete | Raw and inferred adds, custom prompts, conflict actions, procedural memory, expiration, metadata, and scopes are supported. |
| Add multimodal / image memories | Complete | Ingestion of image messages via Vision LLMs and direct image embedding vectors (`IImageEmbeddingGenerator`) are supported. |
| Get one / list | Complete | CRUD, paging, expiration visibility, and expression filters are supported. |
| Update | Complete | Text, metadata, expiration, hash regeneration, entity relinking, graph relinking, and history are supported. |
| Delete / filtered delete all | Complete | Single and filtered bulk deletion are supported. |
| History | Complete | In-memory, SQLite, and PostgreSQL stores persist chronological Add, Update, and Delete events with original/event timestamps, deletion state, actor, and role; built-in atomic stores commit memory rows and history together. |
| Reset | Complete | `ResetAsync` clears memories, history, vectors, entities, and graph state. |
| Sync API | Complete | `SynchronousMemoryService` wraps the native async API, including batch search, paging, and graph relation retrieval. |

## Retrieval

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| Semantic vector search | Complete | In-process cosine fallback and PostgreSQL pgvector search exist. |
| Batch search | Complete | `SearchManyAsync` uses one batch embedding and vector-store call when both providers support batching, with a sequential fallback. |
| Score threshold | Complete | Semantic thresholds are applied consistently after backend retrieval. |
| Metadata filters and operators | Complete | Nested And/Or/Not plus equality, ranges, membership, containment, and existence are supported. |
| BM25 keyword search | Complete | Built-in BM25 scoring is normalized and fused with semantic retrieval. |
| Entity extraction and boosting | Complete | Built-in extraction, linking, cleanup, boosts, and in-memory/PostgreSQL stores exist. |
| Reranking | Complete | `LlmReranker`, `CohereReranker`, `ZeroEntropyReranker`, and local `CrossEncoderReranker` paths are included behind `IMemoryReranker`. |
| Search explanations | Complete | Semantic, keyword, entity/graph, raw, maximum, threshold, and reranker scores are exposed. |
| Memory provenance and retrieval policy | Complete | `MemoryBehavior` and `MemoryType` persist with each memory; ordinary search is factual-only, with explicit opt-in for associative and agent-owned memories. |

## Memory intelligence

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| LLM fact extraction | Complete | Extraction and structured Add/Update/Delete/None conflict decisions are provider-based. |
| Deduplication | Complete | Scope-aware SHA-256 content hashes suppress stored and within-batch duplicates. |
| Entity graph/linking | Complete | Native extraction, linking, boosts, cleanup, and in-memory/PostgreSQL stores exist. |
| Graph memory | Complete | Native graph contracts, LLM triple extraction, in-memory/PostgreSQL storage, boosts, and lifecycle cleanup exist. |
| Procedural memory | Complete | Agent-scoped procedure generation is supported through `IProceduralMemoryGenerator`. |
| Expiration | Complete | Expiration persistence and default expired-memory filtering are supported. |

## Providers

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| LLM providers | Complete | OpenAI-compatible, Anthropic Messages, and native Ollama chat protocols cover hosted, routed, and local model families behind `IChatCompletionClient`. |
| Embedding providers | Complete | Deterministic local, OpenAI-compatible, and native Ollama batch embeddings are included behind `IEmbeddingGenerator`. |
| Vector stores | Complete | In-memory, SQLite, PostgreSQL/pgvector, and Qdrant persistence are included; each preserves the public filtering and vector-search contract. |
| Rerank providers | Complete | LLM, Cohere, ZeroEntropy, and pluggable local cross-encoder reranking are included behind `IMemoryReranker`. |
| Graph providers | Complete | In-memory and PostgreSQL graph stores are included behind `IGraphMemoryStore`. |

## Operations

| Python capability | Status | Mem0Sharp state |
| --- | --- | --- |
| Telemetry | Complete | Opt-in `IMemoryTelemetry` captures content-free operation events. |
| MCP integration | Complete | The `samples/McpServer` project exposes nine local tools over stdio through the official `ModelContextProtocol` .NET SDK. |
| Configuration model | Complete | `MemoryServiceConfiguration` composes all native C# providers. |
| Batch embedding/insertion | Complete | Batch contracts, OpenAI-compatible batch embeddings, local batching, and transactional PostgreSQL batch writes are supported. |
| Atomic memory/history writes | Complete | `IAtomicMemoryStore` is implemented by the built-in in-memory, SQLite, and PostgreSQL stores and covered by rollback tests. |

## Implementation order

Remaining work is deeper integration coverage and optional ecosystem expansion:

1. Expand PostgreSQL integration coverage beyond history to the remaining persistence contracts.
2. Add optional protocol adapters as ecosystem demand warrants without changing the core API.

The hosted Mem0 Platform client is intentionally out of scope. Adding it would turn Mem0Sharp into a wrapper around mem0.ai instead of a standalone C# reimplementation.

Provider count alone does not establish parity. Each provider must pass the same behavioral contract for cancellation, filtering, dimensions, persistence, errors, and disposal.
