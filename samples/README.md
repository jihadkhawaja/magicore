# MagiCore samples

These runnable projects progress from a zero-dependency local setup to on-device ONNX inference, Ollama, model-backed extraction, and durable vector storage.

| Sample | What it demonstrates | Requirements |
| --- | --- | --- |
| [Getting started](GettingStarted/README.md) | Add, search, update, history, and delete | .NET 10 |
| [ONNX Local Inference](OnnxLocal/README.md) | 100% on-device private SLM memory extraction via ONNX Runtime & MEAI | .NET 10 |
| [Ollama](Ollama/README.md) | Local model-backed extraction and embeddings via [OllamaSharp](https://github.com/awaescher/OllamaSharp) | .NET 10, Ollama |
| [SQLite Vector Store](VectorDataSqlite/README.md) | Local embedded vector persistence using `Microsoft.Extensions.VectorData` and `sqlite-vec` | .NET 10 |
| [PostgreSQL pgvector](VectorDataPostgres/README.md) | Enterprise vector persistence using `Microsoft.Extensions.VectorData` with `pgvector` | .NET 10, PostgreSQL |
| [Microsoft Agent Framework memory](AgentFrameworkMemory/README.md) | Use MagiCore as an `AIContextProvider` with `Microsoft.Extensions.VectorData` for a .NET agent | .NET 10, OpenAI API key |
| [Multi-agent group chat](MultiAgentGroupChat/README.md) | Four distinct agents share a chat while keeping isolated long-term memories | .NET 10, OpenAI API key |
| [Memory behaviors](MemoryBehaviors/README.md) | Normal, dreaming, random-thought, and personality-shaped memory | .NET 10, OpenAI API key |
| [3D spatial memory robot](3DSpatialMemoryGodot/README.md) | Godot robot observations, radius-based spatial recall, movement, and persistent pgvector storage | .NET 10, Godot .NET 4.8 dev 3, Docker, OpenAI API key |
| [MCP Server](McpServer/README.md) | Model Context Protocol server for Claude Desktop / Cursor | .NET 10 |

The SQLite and PostgreSQL VectorData samples use `LocalEmbeddingGenerator` with 384 dimensions. When switching them to OpenAI `text-embedding-3-small`, set `VectorDataMemoryStoreOptions.VectorDimensions` to 1536 and recreate any collection previously created with a different dimension. The Godot sample already uses configured OpenAI embeddings and defaults to 1536 dimensions.

Run a sample from the repository root:

```powershell
dotnet run --project .\samples\OnnxLocal\OnnxLocal.csproj
```
