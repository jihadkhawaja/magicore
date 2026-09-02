# Multi-agent group chat sample

This console application creates a shared group chat with a user and four AI agents. Each agent has a distinct personality and an isolated Mem0Sharp long-term memory partition connected through Microsoft Agent Framework `AIContextProvider`:

- **Maya** is warm, empathetic, and optimistic.
- **Atlas** is analytical and constructively skeptical.
- **Pixel** is playful and imaginative.
- **Sage** is calm, concise, and practical.

Every user message starts a round in which all four agents reply. Agents see the shared transcript, while each provider is bound to its own `AgentId`:

- `StoreAIContextAsync` automatically extracts durable, personality-shaped `PersonalMemory` records after normal turns.
- `ProvideAIContextAsync` automatically performs semantic recall and recognizes event-time phrases such as `yesterday`, `last week`, dates, and years.
- Requests beginning with `/at <ISO-8601> <question>` use `SearchAtAsync` to reconstruct historical memory state.

Use `/strict <question>` to test long-term memory without leakage from chat history. The command creates a fresh Agent Framework session for every agent, sends only the isolated question, does not share agents' answers with one another, and prints the exact records each provider supplied. Strict-test turns are neither stored nor added to the group transcript.

## Prerequisites

- .NET 10 SDK
- An OpenAI API key

## Run

From the repository root:

```powershell
Copy-Item .\samples\MultiAgentGroupChat\sampleconfig.example.yaml .\samples\MultiAgentGroupChat\sampleconfig.local.yaml
# Edit sampleconfig.local.yaml with your endpoint, API key, and chat model.
dotnet run --project .\samples\MultiAgentGroupChat\MultiAgentGroupChat.csproj
```

`sampleconfig.local.yaml` is ignored by Git and copied to the output directory when the sample builds.

Type a message as the user, `/strict What's my name?` to verify long-term recall in isolation, `/memories` to inspect each agent's private memories, or `exit` to stop. Each normal user turn makes four reply calls and four memory-extraction calls. The in-memory store is reset when the process exits.