using Microsoft.Extensions.VectorData;

namespace Mem0Sharp;

public static class VectorDataExtensions
{
    public static VectorDataMemoryStore ToMemoryStore(
        this VectorStoreCollection<string, VectorDataMemoryRecord> collection,
        VectorStoreCollection<string, VectorDataHistoryRecord>? historyCollection = null,
        VectorDataMemoryStoreOptions? options = null)
    {
        Guard.NotNull(collection);
        return new VectorDataMemoryStore(collection, historyCollection, options);
    }

    public static VectorDataMemoryStore ToMemoryStore(
        this VectorStore vectorStore,
        VectorDataMemoryStoreOptions? options = null)
    {
        Guard.NotNull(vectorStore);
        return new VectorDataMemoryStore(vectorStore, options);
    }
}
