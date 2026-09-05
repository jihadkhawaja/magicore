# SQLite Vector Store Sample (Microsoft.Extensions.VectorData)

This sample demonstrates how to use **MagiCore** with a local embedded **SQLite** database using Microsoft's official `Microsoft.Extensions.VectorData` abstraction via `Microsoft.SemanticKernel.Connectors.SqliteVec` and `sqlite-vec`.

## Features Demonstrated

- Instantiating a local embedded `SqliteVectorStore` from `Microsoft.SemanticKernel.Connectors.SqliteVec`
- Wrapping it in the core `MagiCore.VectorDataMemoryStore`
- Auto-creating vector virtual tables and audit history tables in a local SQLite file (`memories_sample.db`)
- Storing memories with user identity, metadata, and deterministic local embeddings
- Executing semantic vector similarity search
- Point-in-time updates and viewing audit history

This sample uses `LocalEmbeddingGenerator` with 384 dimensions. If you replace it with OpenAI `text-embedding-3-small`, set `VectorDataMemoryStoreOptions.VectorDimensions` to 1536. The embedding output and collection dimensions must match; recreate the collection after changing dimensions.

## Prerequisites

- .NET 10 SDK
- No external server or Docker daemon required � SQLite and `sqlite-vec` run 100% in-process.

## Running the Sample

```powershell
dotnet run --project samples/VectorDataSqlite/VectorDataSqlite.csproj
```
