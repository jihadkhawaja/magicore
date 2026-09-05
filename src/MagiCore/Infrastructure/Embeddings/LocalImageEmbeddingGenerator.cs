using System.Numerics.Tensors;
using System.Text;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class LocalImageEmbeddingGenerator : IImageEmbeddingGenerator
{
    public int Dimensions { get; }

    public EmbeddingGeneratorMetadata Metadata { get; }

    public LocalImageEmbeddingGenerator(int dimensions = 384)
    {
        if (dimensions < 8) throw new ArgumentOutOfRangeException(nameof(dimensions));
        Dimensions = dimensions;
        Metadata = new EmbeddingGeneratorMetadata("LocalImageEmbeddingGenerator", null, null, dimensions);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<DataContent> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var item in values)
        {
            var vector = GenerateVector(item);
            result.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<float>> GenerateVectorAsync(DataContent content, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<float>>(GenerateVector(content));
    }

    public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<DataContent> contents, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(contents);
        cancellationToken.ThrowIfCancellationRequested();
        var vectors = new IReadOnlyList<float>[contents.Count];
        for (var index = 0; index < contents.Count; index++)
        {
            vectors[index] = GenerateVector(contents[index]);
        }
        return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(vectors);
    }

    private float[] GenerateVector(DataContent content)
    {
        var vector = new float[Dimensions];
        if (content == null) return vector;

        if (content.Data.Length > 0)
        {
            var span = content.Data.Span;
            for (var i = 0; i < span.Length; i++)
            {
                var b = span[i];
                var idx = (uint)(i * 31 + b) % (uint)Dimensions;
                vector[idx] += (b / 255.0f);
            }
        }
        else if (content.Uri is not null)
        {
            var uriStr = content.Uri.ToString();
            var tokens = uriStr.ToLowerInvariant().Split(['/', ':', '.', '?', '&', '=', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var hash = StableHash(token);
                vector[(uint)hash % (uint)Dimensions] += 1f;
                vector[(uint)(hash >> 16) % (uint)Dimensions] += 0.5f;
            }
        }

        var norm = TensorPrimitives.Norm(vector);
        if (norm > 0)
        {
            TensorPrimitives.Divide(vector, norm, vector);
        }
        return vector;
    }

    private static int StableHash(ReadOnlySpan<char> value)
    {
        unchecked
        {
            var hash = 17;
            var utf8Bytes = Encoding.UTF8.GetBytes(value.ToString());
            foreach (var valueByte in utf8Bytes) hash = hash * 31 + valueByte;
            return hash & int.MaxValue;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(LocalImageEmbeddingGenerator) ? this : null;

    public void Dispose()
    {
    }
}
