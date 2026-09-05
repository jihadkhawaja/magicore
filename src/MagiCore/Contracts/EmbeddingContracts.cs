using Microsoft.Extensions.AI;

namespace MagiCore;

public interface IEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
#if NETSTANDARD2_0
    Task<IReadOnlyList<float>> GenerateVectorAsync(string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
#else
    Task<IReadOnlyList<float>> GenerateVectorAsync(string text, CancellationToken cancellationToken = default) =>
        this.GenerateVectorCoreAsync(text, cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        this.GenerateVectorBatchCoreAsync(texts, cancellationToken);
#endif
}

public interface IImageEmbeddingGenerator : IEmbeddingGenerator<DataContent, Embedding<float>>
{
#if NETSTANDARD2_0
    Task<IReadOnlyList<float>> GenerateVectorAsync(DataContent content, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<DataContent> contents, CancellationToken cancellationToken = default);
#else
    Task<IReadOnlyList<float>> GenerateVectorAsync(DataContent content, CancellationToken cancellationToken = default) =>
        this.GenerateVectorCoreAsync(content, cancellationToken);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<DataContent> contents, CancellationToken cancellationToken = default) =>
        this.GenerateVectorBatchCoreAsync(contents, cancellationToken);
#endif
}

public static class EmbeddingGeneratorExtensions
{
    public static async Task<IReadOnlyList<float>> GenerateVectorCoreAsync(
        this IEmbeddingGenerator<string, Embedding<float>> generator,
        string text,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(generator);
        var result = await generator.GenerateAsync([text], cancellationToken: cancellationToken);
        return result.Count == 0 ? [] : result[0].Vector.ToArray();
    }

    public static async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchCoreAsync(
        this IEmbeddingGenerator<string, Embedding<float>> generator,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(generator);
        Guard.NotNull(texts);
        if (texts.Count == 0) return [];
        var result = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);
        var list = new IReadOnlyList<float>[result.Count];
        for (var i = 0; i < result.Count; i++)
        {
            list[i] = result[i].Vector.ToArray();
        }
        return list;
    }

    public static async Task<IReadOnlyList<float>> GenerateVectorCoreAsync(
        this IEmbeddingGenerator<DataContent, Embedding<float>> generator,
        DataContent content,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(generator);
        Guard.NotNull(content);
        var result = await generator.GenerateAsync([content], cancellationToken: cancellationToken);
        return result.Count == 0 ? [] : result[0].Vector.ToArray();
    }

    public static Task<IReadOnlyList<float>> GenerateVectorCoreAsync(
        this IEmbeddingGenerator<DataContent, Embedding<float>> generator,
        ReadOnlyMemory<byte> data,
        string mediaType = "image/jpeg",
        CancellationToken cancellationToken = default)
    {
        return generator.GenerateVectorCoreAsync(new DataContent(data, mediaType), cancellationToken);
    }

    public static Task<IReadOnlyList<float>> GenerateVectorCoreAsync(
        this IEmbeddingGenerator<DataContent, Embedding<float>> generator,
        Uri uri,
        string mediaType = "image/jpeg",
        CancellationToken cancellationToken = default)
    {
        return generator.GenerateVectorCoreAsync(Message.CreateDataContent(uri, mediaType), cancellationToken);
    }

    public static Task<IReadOnlyList<float>> GenerateVectorCoreAsync(
        this IEmbeddingGenerator<DataContent, Embedding<float>> generator,
        string uri,
        string mediaType = "image/jpeg",
        CancellationToken cancellationToken = default)
    {
        return generator.GenerateVectorCoreAsync(Message.CreateDataContent(uri, mediaType), cancellationToken);
    }

    public static async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchCoreAsync(
        this IEmbeddingGenerator<DataContent, Embedding<float>> generator,
        IReadOnlyList<DataContent> contents,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(generator);
        Guard.NotNull(contents);
        if (contents.Count == 0) return [];
        var result = await generator.GenerateAsync(contents, cancellationToken: cancellationToken);
        var list = new IReadOnlyList<float>[result.Count];
        for (var i = 0; i < result.Count; i++)
        {
            list[i] = result[i].Vector.ToArray();
        }
        return list;
    }
}