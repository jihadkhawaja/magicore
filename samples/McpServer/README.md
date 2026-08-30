# Mem0Sharp MCP server

This sample exposes Mem0Sharp's local MCP tools over stdio for the VS Code workspace configuration in `.vscode/mcp.json`.

It uses the official `ModelContextProtocol` .NET SDK for tool discovery, JSON-RPC handling, lifecycle messages, and stdio transport. The core `Mem0Sharp` library remains independent of MCP hosting dependencies.

Memories are stored in-memory using the core `VectorDataMemoryStore` or can be connected to any `Microsoft.Extensions.VectorData` persistent vector store.

Run it manually from the repository root:

```powershell
dotnet run --project .\samples\McpServer\McpServer.csproj
```
