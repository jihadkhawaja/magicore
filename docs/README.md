# MagiCore documentation

MagiCore is a standalone .NET memory library that runs locally with the providers you configure.

## Start here

1. Follow [Getting started](getting-started.md) to install the package and learn the memory lifecycle.
2. Run the [sample projects](../samples/README.md), beginning with the zero-setup console application.
3. Use the [API reference](api-reference.md) when you need filters, scopes, paging, expiration, spatial and robotics memory, or extension interfaces.

## Choose a deployment path

| Goal | Guide |
| --- | --- |
| Build and test without external services | [Getting started](getting-started.md) |
| Use OpenAI, Azure OpenAI, Ollama, or another `Microsoft.Extensions.AI` provider | [Providers and persistence](providers-and-persistence.md#1-openai--azure-openai) |
| Persist vectors in PostgreSQL, SQLite, Qdrant, or another vector store | [Providers and persistence](providers-and-persistence.md#4-microsoftextensionsvectordata-persistence-postgresql-sqlite-azure-ai-search-redis-etc) |
| Build an embodied 3D memory workflow | [Godot spatial memory sample](../samples/3DSpatialMemoryGodot/README.md) |
| Add reranking or custom providers | [Providers and persistence](providers-and-persistence.md#available-model-and-storage-ecosystems) |
| Expose memory through local MCP tools | [Getting started](getting-started.md#expose-local-mcp-tools) |

## Reference and project internals

- [API reference](api-reference.md) describes public contracts and configuration models.
- [Architecture](architecture.md) explains dependency direction and extension boundaries.
- [Evaluation](evaluation.md) describes the benchmark harness and the latest measured results.
- [Contribution guide](../CONTRIBUTING.md) covers local development and pull requests.

## Important defaults

`new MemoryService()` uses in-memory storage, deterministic lexical hashing embeddings, and basic message extraction. This path is useful for development and tests, but it is not a semantic-quality baseline or durable production storage. Choose model-backed embeddings and a persistent store for production workloads, and keep the configured embedding dimensions identical across the provider and vector store.
