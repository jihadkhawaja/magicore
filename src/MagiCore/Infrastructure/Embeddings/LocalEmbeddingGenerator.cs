using System.Numerics.Tensors;
using System.Text;
using Microsoft.Extensions.AI;

namespace MagiCore;

public sealed class LocalEmbeddingGenerator : IEmbeddingGenerator
{
    public int Dimensions { get; }

    public EmbeddingGeneratorMetadata Metadata { get; }

    public LocalEmbeddingGenerator(int dimensions = 384)
    {
        if (dimensions < 8) throw new ArgumentOutOfRangeException(nameof(dimensions));
        Dimensions = dimensions;
        Metadata = new EmbeddingGeneratorMetadata("LocalEmbeddingGenerator", null, null, dimensions);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(values);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var text in values)
        {
            var vector = GenerateVector(text);
            result.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<float>> GenerateVectorAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<float>>(GenerateVector(text));
    }

    public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateVectorBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(texts);
        cancellationToken.ThrowIfCancellationRequested();
        var vectors = new IReadOnlyList<float>[texts.Count];
        for (var index = 0; index < texts.Count; index++)
        {
            vectors[index] = GenerateVector(texts[index]);
        }
        return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>(vectors);
    }

    private float[] GenerateVector(string text)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text)) return vector;

        var tokens = text.ToLowerInvariant().Split([' ', '\t', '\r', '\n', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            var hash = StableHash(token);
            vector[(uint)hash % (uint)Dimensions] += 1f;
            vector[(uint)(hash >> 16) % (uint)Dimensions] += 0.5f;
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
        serviceType == typeof(LocalEmbeddingGenerator) ? this : null;

    public void Dispose()
    {
    }
}
