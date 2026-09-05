# ONNX Runtime Local Inference Sample

This sample demonstrates how to run **MagiCore** with **100% on-device, offline model execution** using ONNX Runtime GenAI and `Microsoft.Extensions.AI`.

## Architecture

```
[Agent / Application]
        │
        ▼
   [MagiCore]
        │
        ├── IChatClient (ONNX Runtime GenAI / Phi-3.5 / Phi-4 ONNX)
        └── IEmbeddingGenerator (Local ONNX / all-MiniLM-L6-v2 ONNX)
```

No external daemons (like Ollama) or cloud endpoints (OpenAI / Azure) are required. All inference runs in-process with hardware acceleration (CPU, DirectML, CUDA, QNN).

## Microsoft Agent Framework Reference

For details on deploying ONNX GenAI models in .NET AI applications, see the official Microsoft guide:
- [Microsoft Agent Framework - ONNX Integration](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/onnx)

## Run the Sample

From the repository root:

```powershell
dotnet run --project .\samples\OnnxLocal\OnnxLocal.csproj
```
