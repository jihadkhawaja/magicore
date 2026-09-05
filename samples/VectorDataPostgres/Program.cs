using MagiCore;
using Microsoft.SemanticKernel.Connectors.PgVector;
using Npgsql;

Console.WriteLine("=== MagiCore with PostgreSQL pgvector (MEVD) ===");
Console.WriteLine();

var connectionString = Environment.GetEnvironmentVariable("MAGICORE_POSTGRES_CONNECTION")
    ?? "Host=localhost;Port=5432;Database=magicore;Username=postgres;Password=postgres";

Console.WriteLine($"Connecting to PostgreSQL: {connectionString}");

try
{
    var vectorStore = new PostgresVectorStore(connectionString);

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

    Console.WriteLine("1. Storing memories for Alice and Bob in PostgreSQL...");
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

    Console.WriteLine("3. Updating Alice's memory in PostgreSQL...");
    var coffeeMemory = searchResults[0].Memory;
    await memory.UpdateAsync(coffeeMemory.Id, "Alice now prefers cold brew coffee with oat milk", new Dictionary<string, string>
    {
        ["preference"] = "beverage",
        ["updated"] = "true"
    });

    Console.WriteLine("   Memory updated.");
    Console.WriteLine();

    Console.WriteLine("4. Inspecting audit history for memory from PostgreSQL history collection...");
    var history = await memory.GetHistoryAsync(coffeeMemory.Id);
    foreach (var entry in history)
    {
        Console.WriteLine($"   - Event: {entry.Event} | Old: \"{entry.OldMemory}\" -> New: \"{entry.NewMemory}\"");
    }
    Console.WriteLine();

    Console.WriteLine("=== PostgreSQL pgvector sample completed successfully! ===");
}
catch (NpgsqlException ex)
{
    Console.WriteLine($"PostgreSQL is not reachable: {ex.Message}");
    Console.WriteLine("Tip: Run `docker compose up -d` in `samples/VectorDataPostgres` to start pgvector.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine("Tip: Ensure PostgreSQL has the pgvector extension enabled.");
}
