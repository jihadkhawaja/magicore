namespace MagiCore;

public sealed class VectorDataMemoryStoreOptions
{
    public string CollectionName { get; set; } = "magicore_memories";
    public string HistoryCollectionName { get; set; } = "magicore_history";
    public int VectorDimensions { get; set; } = 384;
    public bool AutoCreateCollection { get; set; } = true;
}
