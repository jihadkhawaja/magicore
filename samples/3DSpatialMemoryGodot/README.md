# Godot Spatial Memory Robot

A Godot .NET warehouse robot that uses MagiCore to persist timestamped camera observations and recall nearby objects. OpenAI provides image understanding and embeddings; PostgreSQL with pgvector provides durable storage.

## Prerequisites

- .NET 10 SDK.
- Godot .NET 4.8 dev 3, matching the `Godot.NET.Sdk/4.8.0-dev.3` project reference.
- Docker Desktop or another Docker Compose environment.
- An OpenAI API key. The chat model must support images and JSON responses.

The project references the MagiCore source in this checkout through `../../src/MagiCore/MagiCore.csproj`.

## Configure the sample

From `samples/3DSpatialMemoryGodot`, create the ignored local configuration:

```powershell
Copy-Item .\sampleconfig.example.yaml .\sampleconfig.local.yaml
```

Edit `sampleconfig.local.yaml` and replace the API-key placeholder. The OpenAI endpoint must include `/v1`. Keep `postgres.embeddingDimensions` aligned with the embedding model; `text-embedding-3-small` uses the configured 1536 dimensions.

The configured `tableName` is used as a prefix. With the example value, the sample creates `sample_memories_spatial` and `sample_memories_spatial_history` collections.

## Start PostgreSQL

Run from the sample directory with Docker running:

```powershell
docker compose up -d --wait
docker compose ps
```

The dedicated `godot-spatial-memory-postgres-1` container uses `pgvector/pgvector:pg17`. It initializes the vector extension and stores data in the `godot-spatial-memory_postgres-data` named volume. Other projects' containers and databases are not modified.

| Setting | Development value |
| --- | --- |
| Address | `127.0.0.1:55432` |
| Database | `godot_spatial_memory` |
| Username | `postgres` |
| Password | `postgres` |

These credentials are for local development only. The published port is restricted to loopback.

## Run the robot

Open `project.godot` in the Godot editor and run the main scene. The robot starts paused.

- **Observe** captures the current camera image, recalls nearby observations, requests one bounded model decision, moves, and writes new spatial observations.
- **Run** performs up to 30 autonomous decisions and stops when the model chooses `wait`.
- **Stop** cancels the pending decision and movement.
- **Recall Memories** loads nearby persisted observations without making a model request.

Each observation is stored through `RememberSpatialAsync` with map, world position, observation time, confidence, and optional detected-object position. `RecallSpatialAsync` returns up to 12 observations within 35 meters for the same map, user, and agent, ordered by distance and recency. Model and embedding calls incur API usage charges.

This sample demonstrates the foundational spatial-observation API. MagiCore also provides robotics evidence reconstruction and measured action-episode APIs for applications with external object tracking, versioned coordinate frames, visibility coverage, uncertainty estimates, and controller feedback. See [Spatial memory](../../docs/api-reference.md#spatial-memory) in the API reference.

## Smoke tests

Build the C# project from the sample directory:

```powershell
dotnet build .\MagiCore-Spatial-Memory.csproj
```

Run the offline smoke test with your Godot console executable:

```powershell
& "C:\path\to\Godot_v4.8-dev3_mono_windows_arm64_console.exe" --path . -- --smoke-test
```

This checks scene loading, rendering, movement, and wall collisions without loading configuration or calling external services. A successful run prints `SMOKE PASS`.

After configuring OpenAI and starting PostgreSQL, run the complete persistence loop:

```powershell
& "C:\path\to\Godot_v4.8-dev3_mono_windows_arm64_console.exe" --path . -- --live-smoke
```

The live test also performs a model decision, writes observations, creates a new database client, and verifies persisted spatial recall. A successful run prints `LIVE PASS`. Screenshots are written under `artifacts/`.

## Stop or reset the database

```powershell
docker compose stop
docker compose up -d --wait
```

Stopping or recreating the container preserves memories. Do not run `docker compose down --volumes` unless you intend to delete the sample database.