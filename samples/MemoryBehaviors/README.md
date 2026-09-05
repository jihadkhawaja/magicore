# Memory behaviors sample

This sample sends one conversation through all four optional memory behaviors:

- `Normal` keeps the existing durable-fact extraction behavior.
- `Dreaming` consolidates themes, emotions, and tentative associations.
- `RandomThoughts` creates useful or surprising thoughts inspired by the conversation.
- `PersonalMemory` writes from the agent's first-person perspective and can use `Prompt` as its personality.

The non-normal modes are a MagiCore extension to conventional memory extraction. Instead of limiting memory to a neutral list of user facts, an application can opt into reflective associations or an agent-owned point of view while keeping uncertain model output explicitly tentative.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An OpenAI API key

Copy the example configuration, then put your API key in the ignored local file:

```powershell
Copy-Item .\samples\MemoryBehaviors\sampleconfig.example.yaml .\samples\MemoryBehaviors\sampleconfig.local.yaml
```

`sampleconfig.local.yaml` is ignored by Git. It configures the OpenAI endpoint, API key, chat model, and embedding model used by this sample.

## Run it

From the repository root:

```powershell
dotnet run --project .\samples\MemoryBehaviors\MemoryBehaviors.csproj
```

Behavior shaping requires inference and a behavior-aware extractor. The built-in `LlmMemoryExtractor` supports it. `Infer = false` always preserves input verbatim, and omitting `Behavior` is equivalent to `MemoryBehavior.Normal`.