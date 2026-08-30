using System;
using System.IO;
using System.Runtime.InteropServices;
using Mem0Sharp;
using Mem0Sharp.VectorData;
using Microsoft.SemanticKernel.Connectors.SqliteVec;

Console.WriteLine("=== Mem0Sharp with SQLite Vector Store (MEVD) ===");
Console.WriteLine();

// Ensure native sqlite-vec (vec0.dll / vec0.so / vec0.dylib) is discoverable
var runtimeFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64")
    : (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64") : "linux-x64");

var nativeDir = Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeFolder, "native");
if (Directory.Exists(nativeDir))
{
    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
    Environment.SetEnvironmentVariable("PATH", nativeDir + Path.PathSeparator + currentPath);

    var src = Path.Combine(nativeDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "vec0.dll" : "vec0.so");
    var dst = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(src));
    if (File.Exists(src) && !File.Exists(dst))
    {
        try { File.Copy(src, dst, true); } catch { }
    }
}

var dbPath = "memories_sample.db";
if (File.Exists(dbPath))
{
    File.Delete(dbPath);
}

var connectionString = $"Data Source={dbPath}";
var vectorStore = new SqliteVectorStore(connectionString);

var store = new VectorDataMemoryStore(vectorStore, new VectorDataMemoryStoreOptions
{
    CollectionName = "user_memories",
    HistoryCollectionName = "user_memories_history",
    VectorDimensions = 384,
    AutoCreateCollection = true
});

await store.InitializeAsync();

var embeddings = new LocalEmbeddingGenerator(dimensions: 384);
var memory = new MemoryService(store: store, embeddings: embeddings);

Console.WriteLine("1. Storing memories for Alice and Bob in SQLite...");
await memory.AddAsync("Alice loves dark roast coffee in the morning", new MemoryAddOptions
{
    UserId = "alice",
    Metadata = new Dictionary<string, string> { ["preference"] = "beverage" }
});

await memory.AddAsync("Alice is a senior C# and .NET engineer working on AI agents", new MemoryAddOptions
{
    UserId = "alice",
    Metadata = new Dictionary<string, string> { ["role"] = "engineering" }
});

await memory.AddAsync("Bob prefers green tea and writes Rust", new MemoryAddOptions
{
    UserId = "bob",
    Metadata = new Dictionary<string, string> { ["role"] = "engineering" }
});

Console.WriteLine("   Memories stored successfully.");
Console.WriteLine();

Console.WriteLine("2. Performing vector similarity search for Alice...");
var searchResults = await memory.SearchAsync("What coffee does Alice drink?", new MemorySearchOptions
{
    Filter = new MemoryFilter(UserId: "alice"),
    TopK = 2
});

foreach (var result in searchResults)
{
    Console.WriteLine($"   - [{result.Score:F4}] {result.Memory.Text} (Scope: {result.Memory.Scope})");
}
Console.WriteLine();

Console.WriteLine("3. Updating Alice's memory...");
var coffeeMemory = searchResults[0].Memory;
await memory.UpdateAsync(coffeeMemory.Id, "Alice now prefers cold brew coffee with oat milk", new Dictionary<string, string>
{
    ["preference"] = "beverage",
    ["updated"] = "true"
});

Console.WriteLine("   Memory updated.");
Console.WriteLine();

Console.WriteLine("4. Inspecting audit history for memory from SQLite history collection...");
var history = await memory.GetHistoryAsync(coffeeMemory.Id);
foreach (var entry in history)
{
    Console.WriteLine($"   - Event: {entry.Event} | Old: \"{entry.OldMemory}\" -> New: \"{entry.NewMemory}\"");
}
Console.WriteLine();

Console.WriteLine("=== SQLite Vector Store sample completed successfully! ===");
