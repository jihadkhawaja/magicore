# Getting started sample

This console application demonstrates the complete memory lifecycle using MagiCore's in-memory defaults. It requires no API key, model server, or database.

## Run it

From the repository root:

```powershell
dotnet run --project .\samples\GettingStarted\GettingStarted.csproj
```

The sample adds and searches a user-scoped memory, updates it, prints its audit history, and deletes it.

The default `LocalEmbeddingGenerator` is a deterministic lexical hashing implementation intended for development and tests. For model-backed embeddings, continue with the [Ollama sample](../Ollama/README.md). For durable storage, use the [PostgreSQL sample](../VectorDataPostgres/README.md).
