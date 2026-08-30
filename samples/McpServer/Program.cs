using Mem0Sharp;
using Mem0Sharp.McpSample;
using Mem0Sharp.VectorData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var store = VectorDataMemoryStore.CreateInMemory(new VectorDataMemoryStoreOptions
{
    CollectionName = "mcp_memories"
});
await store.InitializeAsync();

var memory = new MemoryService(store);
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Services.AddSingleton<IMemoryService>(memory);
builder.Services
	.AddMcpServer()
	.WithStdioServerTransport()
	.WithTools<McpTools>();

await builder.Build().RunAsync();
