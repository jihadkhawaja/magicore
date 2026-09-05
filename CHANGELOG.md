# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

## [v1.0.0] - 2026-09-05

### Breaking Changes
- **Renamed to MagiCore**: Renamed the `Mem0Sharp` package, assembly, root namespace, solution, projects, tests, evaluation harness, and repository paths to `MagiCore`. Consumers must replace the old package reference and update `using Mem0Sharp` directives to `using MagiCore`.

### Added
- **3D Spatial Memory**: Added provider-neutral APIs for storing timestamped 3D observations and recalling them by map, user or agent scope, Euclidean radius, observation time, confidence, and entity identity.
- **Robotics Object Memory**: Added event-time reconstruction of object beliefs from frame-aware sensor evidence, including visibility, freshness, uncertainty, conflict, relocation, conservative association, and spatial-relation states.
- **Robot Action Episodes**: Added persistence and recall for controller-reported action attempts with measured poses, outcomes, feedback, and spatial, temporal, action, and heading filters.
- **Godot Robot Sample**: Added a 3D warehouse robot sample using OpenAI vision and embeddings with PostgreSQL/pgvector persistence, plus offline and live memory checks.

### Fixed
- **VectorData Persistence Enumeration**: Fixed `VectorDataMemoryStore.GetAllAsync` so a new store instance can enumerate persisted records with scope filtering.

### Documentation
- Reworked the README and project artwork for the MagiCore identity, and documented spatial memory, robotics evidence, action-history boundaries, architecture, and Godot sample setup.

## [v0.3.1] - 2026-09-02

### Added
- **Event-Time Retrieval**: Added optional reference timestamps, explicit time ranges, and confidence-gated deterministic query interpretation without requiring persistence schema changes.
- **Evaluation Harness**: Expanded the evaluation suite with deterministic capability checks, a self-contained four-domain longitudinal dataset, configurable quality scenarios, event-time comparisons, dataset validation, and JSON/Markdown reporting.
- **Multi-Agent Group Chat Sample**: Added an Agent Framework sample with isolated per-agent long-term memories, personality-driven participants, temporal and point-in-time recall, strict isolated-memory testing, and private memory inspection.

## [v0.3.0] - 2026-08-30

### ⚠️ Breaking Changes
- **Single-Package Architecture with `Microsoft.Extensions.VectorData` Consolidation**: Consolidated all storage and vector database operations directly into the core `MagiCore` package. Removed legacy `MagiCore.PostgreSQL`, `MagiCore.SQLite`, and `MagiCore.VectorData` satellite packages.
- Any MEVD connector (`Microsoft.SemanticKernel.Connectors.*`, `CommunityToolkit.AI.VectorStore.*`) can now be plugged directly into `VectorDataMemoryStore` (built into `MagiCore`) to support PostgreSQL/pgvector, SQLite, Azure AI Search, Redis, Qdrant, Milvus, Pinecone, and more.

### Added
- **Point-in-Time Memory Reads**: Added `SearchAtAsync`, `GetAllAtAsync`, and opt-in `ITemporalMemoryStore` support for non-destructive historical queries.
- **Filtered Rollback**: Corrected `RollbackAsync` filtering so recovery can be limited to matching users, metadata subjects, and other memory scopes while preserving complete snapshots and embeddings.

### Improved
- **Single Unified Package**: Installing `dotnet add package MagiCore` provides the full feature set without requiring additional provider packages.
- Updated `samples/McpServer`, `samples/AgentFrameworkMemory`, and `evaluation/MagiCore.Evaluation` to use `VectorDataMemoryStore`.

---

## [v0.2.2] - 2026-08-22

### Added
- Multi-targeted the core package for .NET Standard 2.0, .NET 8, .NET 9, and .NET 10.
- Added per-target test execution plus runtime smoke coverage for the .NET Standard 2.0 package assets.

---

## [v0.2.1] - 2026-08-16

### Added
- **Admission Gate Defense Layer**: Added `NoveltyAdmissionGate`, `PromptInjectionAdmissionGate`, and `CompositeAdmissionGate` to reject duplicate or malicious memory candidates before persistence.
- **Trajectory Tracking**: Extended `MemoryService` and the public contracts with trajectory persistence support, including `AppendTrajectoryAsync`, `GetTrajectoriesAsync`, and on-demand extraction from stored trajectories.
- **History Rollback Support**: Added rollback and history restoration APIs to in-memory and persistence-backed stores, including `RollbackAsync` and `RollbackToHistoryAsync`.

### Improved
- **Memory Lifecycle Safety**: Hardened consolidation and stale-memory workflows by combining rollback/trajectory tracking with stricter admission filtering and verification paths.
- **Test Coverage**: Expanded unit tests for admission gates, consolidation behavior, and memory behavior scenarios to cover the new safeguards and lifecycle features.

---

## [v0.2.0] - 2026-08-15

### ⚠️ Breaking Changes
- **Storage Interface Consolidation**: Merged 8 fragmented storage interfaces (`IVectorMemoryStore`, `IBulkMemoryStore`, `IBatchMemoryStore`, `IAtomicMemoryStore`, `IBatchVectorMemoryStore`, `IMemoryHistoryStore`, `IResettableMemoryStore`) into a single cohesive [`IMemoryStore`](src/MagiCore/Contracts/StorageContracts.cs).
- **Embedding Generator Consolidation**: Merged `IBatchEmbeddingGenerator` into [`IEmbeddingGenerator`](src/MagiCore/Contracts/EmbeddingContracts.cs) with default interface fallback.
- **Memory Extractor Consolidation**: Merged `IBehaviorAwareMemoryExtractor` into [`IMemoryExtractor`](src/MagiCore/Contracts/IntelligenceContracts.cs) with `ExtractAsync(messages, options, ct)`.
- **Canonical Service Signatures**: Standardized [`IMemoryService`](src/MagiCore/Contracts/ServiceContracts.cs) and `MemoryService` around canonical options records (`MemoryAddOptions`, `MemorySearchOptions`, `MemoryPageOptions`, `MemoryUpdate`).

### Added
- **SIMD Hardware Acceleration**: Integrated `System.Numerics.Tensors` across vector cosine similarity (`TensorPrimitives.CosineSimilarity`), vector normalization (`TensorPrimitives.Norm`), and vector scaling (`TensorPrimitives.Divide`).
- **Native Qdrant REST Search**: Implemented native REST vector search via `POST collections/{name}/points/search` and `POST collections/{name}/points/search/batch` with payload filter translation.
- **SQLite SQL Pushdown**: Pushed down `user_id`, `agent_id`, `run_id`, `scope`, `behavior`, `memory_type`, and `expires_at` filters directly into parameterized SQL `WHERE` clauses, streaming records via `IAsyncEnumerable<Memory>`.
- **PostgreSQL Graph Store Query Pushdown**: Term pattern matching for graph boost calculations pushed down to database index scans with `ILIKE ANY($1)` instead of full table scans.
- **Concurrent LLM Reranking**: Added bounded concurrent scoring with `Parallel.ForEachAsync` (`MaxDegreeOfParallelism = 8`) in `LlmReranker`.
- **Reverse Index in In-Memory Entity Store**: Added reverse lookup index (`memoryId -> HashSet<string>`) to achieve $O(1)$ memory deletions.
- **Resilient JSON Parsing**: Added markdown fence stripping (```` ```json ````) and JSON array slice extractors in `LlmMemoryExtractor` and `LlmGraphMemoryExtractor`.
- **Strongly-Typed API DTOs**: Migrated `OpenAiCompatibleClient`, `AnthropicClient`, and `OllamaClient` from generic `JsonNode` heap allocations to strongly-typed DTO records and safe base URI combining.

### Optimized
- **BM25 Hybrid Search Complexity**: Precomputed document frequencies reduced BM25 search complexity from $O(D^2 \cdot T)$ to $O(D \cdot T)$ with zero-allocation span tokenization.
- **In-Memory Streaming**: Removed unnecessary `Task.Yield()` state machine overhead in `InMemoryStore.GetAllAsync`.

---

## [v0.1.7] - 2026-08-15

### Added
- **Long-term memory lifecycle**:
  - Recency-aware retrieval (`ApplyRecencyBias`) during search.
  - Freshness filtering for newer vs. stale facts (`FreshnessWindow`).
  - Stale memory forgetting (`ForgetStaleAsync`) for outdated or superseded facts.
  - Preference consolidation (`ConsolidateAsync`) for preference drift and memory refinement.
- **Enhanced evaluation suite**:
  - Added realistic long-horizon (`realistic-long-haul`) and stale-forgetting (`stale-forget`) benchmark scenarios.
  - Stricter threshold testing and multi-session behavioral memory evaluation.
- Updated documentation and published benchmark results matching verified benchmark runs.

---

## [v0.1.6] - 2026-08-11

### Added
- **Package split**: Split persistence providers into separate NuGet packages:
  - `MagiCore` (dependency-free core)
  - `MagiCore.PostgreSQL`
  - `MagiCore.SQLite`
- Atomic memory and history persistence for built-in stores.
- Memory provenance with behavior-aware retrieval.
- Factual search excludes associative memories by default.
- Fail-closed entity and graph enrichment.
- Updated OpenAI configuration defaults to `gpt-5.6-luna` (with `text-embedding-3-small` for embeddings).
- External evaluation dataset support and confidence intervals.
- Pinned SQLite runtime dependencies to patched versions.

---

## [v0.1.5] - 2026-08-01

### Added
- Configurable memory behaviors: `Normal`, `Dreaming`, `RandomThoughts`, and `PersonalMemory`.
- Behavior-aware extraction through `IBehaviorAwareMemoryExtractor`.
- Behavior and persona prompts for LLM-based memory extraction.
- MCP support for `behavior` and `prompt` options in `add_memory`.
- Runnable `MemoryBehaviors` sample.
- Unit tests for memory behaviors and MCP integration.

### Documentation
- Expanded README and API documentation with memory behavior details.
- Added getting-started guidance and examples for behavior-shaped memories.

---

## [v0.1.4] - 2026-08-01

### Added
- Qdrant memory store with configurable options.
- Cohere, CrossEncoder, and ZeroEntropy reranker providers.
- Anthropic and Ollama model clients.
- Getting Started, Ollama, and Postgres/OpenAI samples.
- YAML configuration for OpenAI integration tests.

### Improved
- Expanded batch memory operations and history persistence.
- Enhanced PostgreSQL and OpenAI integration coverage.
- Added provider and reranker tests.
- Updated API, provider, persistence, capability, and onboarding documentation.

---

## [v0.1.3] - 2026-07-26

### Added
- PostgreSQL memory history tracking for update and delete operations.
- Batch persistence through `SaveBatchAsync`.
- PostgreSQL entity and graph relationship stores.
- Expiration support with `expires_at` and `hash_value` fields.
- Nested logical expressions and numeric comparisons in filtering.
- `LlmMemoryConflictResolver` with resilient LLM response parsing.
- Unit test coverage for LLM conflict resolution and expanded memory service scenarios.

### Architecture
- Reorganized project into application, contracts, domain, infrastructure, intelligence, telemetry, and transport layers.
- Added explicit domain models and service contracts.
- Added contributor guide (`CONTRIBUTING.md`) and security policy (`SECURITY.md`).

---

## [v0.1.2] - 2026-07-14

### Changed
- Updated copyright information and licensing metadata.

---

## [v0.1.1] - 2026-07-13

### Changed
- Configured NuGet trusted publishing workflows.

---

## [v0.1.0] - 2026-07-13

### Added
- Initial release of MagiCore: Long-term memory for AI applications in .NET with semantic search and replaceable embedding and storage providers.
- GitHub Actions workflow for publishing NuGet packages and project metadata.
