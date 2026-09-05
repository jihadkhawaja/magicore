using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace MagiCore.Tests;

public sealed class MultimodalMemoryTests
{
    [Fact]
    public void Message_FromImage_Uri_ConvertsToChatMessageWithDataContent()
    {
        var uri = new Uri("https://example.com/photo.jpg");
        var msg = Message.FromImage(uri, "image/jpeg");

        Assert.Equal("user", msg.Role);
        Assert.NotNull(msg.Contents);
        Assert.Single(msg.Contents!);
        Assert.IsType<DataContent>(msg.Contents![0]);

        var chatMessage = msg.ToChatMessage();
        Assert.Equal(ChatRole.User, chatMessage.Role);
        Assert.Single(chatMessage.Contents);
        var dataContent = Assert.IsType<DataContent>(chatMessage.Contents[0]);
        Assert.Equal(uri.ToString(), Encoding.UTF8.GetString(dataContent.Data.ToArray()));
        Assert.Equal("image/jpeg", dataContent.MediaType);
    }

    [Fact]
    public void Message_FromTextAndImage_PreservesBothContents()
    {
        var bytes = Encoding.UTF8.GetBytes("fake-image-bytes");
        var msg = Message.FromTextAndImage("Check this receipt", bytes, "image/png", "user");

        Assert.Equal("Check this receipt", msg.Content);
        Assert.Equal(2, msg.Contents!.Count);
        Assert.IsType<TextContent>(msg.Contents[0]);
        Assert.IsType<DataContent>(msg.Contents[1]);

        var chatMessage = msg.ToChatMessage();
        Assert.Equal(2, chatMessage.Contents.Count);

        var roundTripped = Message.FromChatMessage(chatMessage);
        Assert.Equal("Check this receipt", roundTripped.Content);
        Assert.Equal(2, roundTripped.Contents!.Count);
    }

    [Fact]
    public async Task LocalImageEmbeddingGenerator_GeneratesDeterministicVectors()
    {
        var generator = new LocalImageEmbeddingGenerator(128);
        Assert.Equal(128, generator.Dimensions);

        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var dataContent = new DataContent(bytes, "image/png");

        var vector1 = await generator.GenerateVectorAsync(dataContent);
        var vector2 = await generator.GenerateVectorAsync(dataContent);

        Assert.Equal(128, vector1.Count);
        Assert.Equal(vector1, vector2);

        var uriContent = Message.CreateDataContent(new Uri("https://example.com/receipt.png"), "image/png");
        var uriVector = await generator.GenerateVectorAsync(uriContent);
        Assert.Equal(128, uriVector.Count);

        var batch = await generator.GenerateVectorBatchAsync([dataContent, uriContent]);
        Assert.Equal(2, batch.Count);
        Assert.Equal(vector1, batch[0]);
        Assert.Equal(uriVector, batch[1]);
    }

    [Fact]
    public async Task LlmMemoryExtractor_PreservesMultimodalContentsInChatPrompt()
    {
        var receivedMessages = new List<ChatMessage>();
        var mockClient = new MockMultimodalChatClient((messages) =>
        {
            receivedMessages.AddRange(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new[] { "User has an invoice for $500", "User bought an Apple display" }))));
        });

        var extractor = new LlmMemoryExtractor(mockClient);
        var imageBytes = Encoding.UTF8.GetBytes("sample-image-data");
        var messages = new[]
        {
            new Message("user", "Here is my invoice"),
            Message.FromImage(imageBytes, "image/png")
        };

        var extracted = await extractor.ExtractAsync(messages);

        Assert.Equal(2, extracted.Count);
        Assert.Equal("User has an invoice for $500", extracted[0].Text);
        Assert.Equal("User bought an Apple display", extracted[1].Text);

        // Verify that the prompt sent to IChatClient contained the DataContent
        var hasDataContent = receivedMessages.Any(m => m.Contents.Any(c => c is DataContent));
        Assert.True(hasDataContent, "Expected ChatMessage with DataContent to be passed to IChatClient");
    }

    [Fact]
    public async Task MemoryService_AddAndSearchWithImageEmbeddings()
    {
        var imageEmbeddings = new LocalImageEmbeddingGenerator(64);
        var textEmbeddings = new LocalEmbeddingGenerator(64);
        var service = new MemoryService(
            embeddings: textEmbeddings,
            imageEmbeddings: imageEmbeddings);

        var imageBytes1 = new byte[] { 10, 20, 30, 40, 50 };
        var imageBytes2 = new byte[] { 90, 80, 70, 60, 50 };

        var addResult = await service.AddAsync(imageBytes1, "image/png", new MemoryAddOptions
        {
            UserId = "user1",
            Prompt = "User uploaded a receipt for lunch",
            Infer = false
        });

        Assert.Single(addResult.Memories);
        var savedMemory = addResult.Memories[0];
        Assert.Equal("User uploaded a receipt for lunch", savedMemory.Text);
        Assert.Equal("image/png", savedMemory.Metadata["media_type"]);

        // Search by exact image bytes
        var searchResults = await service.SearchAsync(imageBytes1, "image/png", new MemorySearchOptions
        {
            Filter = new MemoryFilter(UserId: "user1"),
            TopK = 5
        });

        Assert.NotEmpty(searchResults);
        Assert.Equal(savedMemory.Id, searchResults[0].Memory.Id);
        Assert.True(searchResults[0].Score > 0.99);

        // Synchronous wrapper test
        var syncService = new SynchronousMemoryService(service);
        var syncSearch = syncService.Search(new DataContent(imageBytes1, "image/png"));
        Assert.NotEmpty(syncSearch);
    }

    private sealed class MockMultimodalChatClient : IChatClient
    {
        private readonly Func<IEnumerable<ChatMessage>, Task<ChatResponse>> handler;

        public MockMultimodalChatClient(Func<IEnumerable<ChatMessage>, Task<ChatResponse>> handler)
        {
            this.handler = handler;
        }

        public ChatClientMetadata Metadata => new("MockMultimodalChatClient");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return handler(chatMessages);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(MockMultimodalChatClient) ? this : null;

        public void Dispose()
        {
        }
    }
}
