# PostgreSQL pgvector Sample (Microsoft.Extensions.VectorData)

This sample demonstrates how to use **Mem0Sharp** with **PostgreSQL** and **pgvector** using Microsoft's official `Microsoft.Extensions.VectorData` abstraction via `Microsoft.SemanticKernel.Connectors.PgVector`.

## Features Demonstrated

- Instantiating a `PostgresVectorStore` from `Microsoft.SemanticKernel.Connectors.PgVector`
- Wrapping it in the core `Mem0Sharp.VectorDataMemoryStore`
- Auto-creating vector and audit history tables in PostgreSQL
- Storing memories with user identity, metadata, and deterministic local embeddings
- Executing semantic vector similarity search
- Point-in-time updates and viewing audit history

This sample uses `LocalEmbeddingGenerator` with 384 dimensions. If you replace it with OpenAI `text-embedding-3-small`, set `VectorDataMemoryStoreOptions.VectorDimensions` to 1536. The embedding output and collection dimensions must match; recreate the collection after changing dimensions.

## Prerequisites

1. .NET 10 SDK
2. PostgreSQL 15+ with the `pgvector` extension installed.

To quickly spin up a local PostgreSQL instance with pgvector via Docker:

```powershell
docker compose up -d
```

## Running the Sample

```powershell
dotnet run --project samples/VectorDataPostgres/VectorDataPostgres.csproj
```

By default, the sample connects to `Host=localhost;Port=5432;Database=mem0;Username=postgres;Password=postgres`.
You can customize the connection string using the `MEM0_POSTGRES_CONNECTION` environment variable:

```powershell
$env:MEM0_POSTGRES_CONNECTION = "Host=my-postgres-host;Port=5432;Database=mydb;Username=myuser;Password=mypass"
dotnet run --project samples/VectorDataPostgres/VectorDataPostgres.csproj
```
