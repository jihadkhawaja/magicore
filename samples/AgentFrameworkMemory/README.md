# Microsoft Agent Framework Memory Sample

This sample adds long-term memory to a Microsoft Agent Framework `ChatClientAgent` through `AIContextProvider`. It uses an in-memory `Microsoft.Extensions.VectorData` store for a self-contained example.

- `StoreAIContextAsync` saves each completed user/assistant turn.
- `ProvideAIContextAsync` injects up to five recent memories for the current user.
- `MemoryFilter(UserId: ...)` keeps memories isolated between users.

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

Tell the agent something it should remember (e.g., *"I love dark mode and C#"*), then ask about it in a subsequent turn (e.g., *"What language do I like?"*). Type `exit` to stop.

The in-memory store is reset when the process exits. Replace `VectorDataMemoryStore.CreateInMemory` with a persistent MEVD connector for production applications.
