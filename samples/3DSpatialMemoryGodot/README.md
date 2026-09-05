# Godot Spatial Memory Robot

A Godot .NET warehouse robot using Mem0Sharp spatial observations, OpenAI camera understanding, and persistent pgvector storage.

## Local Database

Run from this project directory with Docker Desktop running:

```powershell
docker compose up -d --wait
docker compose ps
```

The dedicated `godot-spatial-memory-postgres-1` container uses `pgvector/pgvector:pg17`. It initializes the vector extension and stores data in the `godot-spatial-memory_postgres-data` named volume. Other projects' containers and databases are not modified.

- Address: `127.0.0.1:55432`
- Database: `godot_spatial_memory`
- Development username and password: `postgres`

These credentials are for local development only. The published port is restricted to loopback.

```powershell
docker compose stop
docker compose up -d --wait
```

Stopping or recreating the container preserves memories. Do not use `docker compose down --volumes` unless you intend to delete its database.

## Robot

Use a .NET-enabled Godot editor and the .NET 10 SDK. The project references the neighboring Mem0Sharp repository.

The robot loads `sampleconfig.local.yaml` from this directory first. This file is ignored and must not be committed. `sampleconfig.example.yaml` contains the matching database settings and an API-key placeholder. The OpenAI endpoint must include `/v1`; the configured chat model must support images and JSON responses.

Open `project.godot` in Godot and run the scene. The robot starts paused. Single-step performs one camera observation, nearby-memory recall, bounded movement decision, and spatial-memory write. Model and embedding calls incur API usage charges.

To verify the complete loop with your Godot console executable:

```powershell
Godot_console.exe --path . -- --live-smoke
```

The live smoke test checks rendering, movement, wall collisions, and spatial recall from a newly created database client. Use `--smoke-test` instead for the rendering and movement checks without network calls. Screenshots are written under `artifacts/`.