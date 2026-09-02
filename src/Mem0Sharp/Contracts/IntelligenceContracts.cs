using Microsoft.Extensions.AI;

namespace Mem0Sharp;

public interface IMemoryExtractor
{
    Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default);
}

public interface IMemoryReranker
{
    Task<IReadOnlyList<SearchResult>> RerankAsync(string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default);
}

public interface ITemporalQueryInterpreter
{
    Task<TemporalQueryInterpretation?> InterpretAsync(
        string query,
        DateTimeOffset referenceTime,
        CancellationToken cancellationToken = default);
}

public interface ICrossEncoderScorer
{
    Task<IReadOnlyList<double>> ScoreAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default);
}

public interface IMemoryConflictResolver
{
    Task<IReadOnlyList<MemoryDecision>> ResolveAsync(IReadOnlyList<Message> messages, IReadOnlyList<Memory> existingMemories, MemoryAddOptions options, CancellationToken cancellationToken = default);
}

public interface IProceduralMemoryGenerator
{
    Task<string> GenerateAsync(IReadOnlyList<Message> messages, string? prompt = null, CancellationToken cancellationToken = default);
}

public interface IChatCompletionClient
{
    Task<string> CompleteAsync(IReadOnlyList<Message> messages, CancellationToken cancellationToken = default);
}