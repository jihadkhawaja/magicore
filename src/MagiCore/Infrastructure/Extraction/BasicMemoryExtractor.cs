namespace MagiCore;

public sealed class BasicMemoryExtractor : IMemoryExtractor
{
    public Task<IReadOnlyList<MemoryInput>> ExtractAsync(IReadOnlyList<Message> messages, MemoryAddOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var inputs = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new MemoryInput(message.Content.Trim(), Metadata: new Dictionary<string, string> { ["role"] = message.Role }))
            .ToArray();
        return Task.FromResult<IReadOnlyList<MemoryInput>>(inputs);
    }
}
