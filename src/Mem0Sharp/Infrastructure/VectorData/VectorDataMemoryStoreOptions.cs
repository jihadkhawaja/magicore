namespace Mem0Sharp;

public sealed class VectorDataMemoryStoreOptions
{
    public string CollectionName { get; set; } = "mem0_memories";
    public string HistoryCollectionName { get; set; } = "mem0_history";
    public int VectorDimensions { get; set; } = 384;
    public bool AutoCreateCollection { get; set; } = true;
}
