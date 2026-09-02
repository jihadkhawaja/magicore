# Microsoft Agent Framework Memory Sample

This sample adds searchable, temporal long-term memory to a Microsoft Agent Framework `ChatClientAgent` through `AIContextProvider`. It uses an in-memory `Microsoft.Extensions.VectorData` store for a self-contained example.

- `StoreAIContextAsync` extracts durable, personality-shaped `PersonalMemory` records from each completed turn and timestamps them with `ReferenceTime`.
- `ProvideAIContextAsync` uses semantic search rather than injecting every recent memory.
- Natural phrases such as `yesterday`, `last week`, dates, and years enable event-time filtering.
- `/at <ISO-8601> <question>` uses `SearchAtAsync` to reconstruct what the memory store contained at that instant.
- `MemoryFilter(UserId, AgentId, Scope)` keeps the agent's memories isolated.
- `/memories` displays the current private memory records, behavior, and timestamps.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key

## Run

From the repository root:

```powershell
$env:OPENAI_API_KEY = "your-api-key"
# Optional; defaults to gpt-5.6-luna
$env:OPENAI_MODEL = "gpt-5.6-luna"
dotnet run --project .\samples\AgentFrameworkMemory\AgentFrameworkMemory.csproj
```

Try this sequence:

1. Say *"My name is Jay and I prefer dark mode."*
2. Ask *"What display theme do I prefer?"* to exercise semantic search.
3. Ask *"What did I tell you today?"* to exercise event-time search.
4. Use `/at 2000-01-01T00:00:00Z What did you know about me?` to query a snapshot before the memory existed.
5. Use `/at 2099-01-01T00:00:00Z What did you know about me?` to query a snapshot after it existed.
6. Type `/memories` to inspect stored records, or `exit` to stop.

The in-memory store is reset when the process exits. Replace `VectorDataMemoryStore.CreateInMemory` with a persistent MEVD connector for production applications.
