using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Snap.Nicole.Tests;

internal sealed class ReasoningTestClient : IChatClient
{
    private int requests;
    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() { }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        LastMessages = messages.ToArray();
        requests++;
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-" + requests,
            Contents = [new TextReasoningContent("reasoning"), new TextContent("answer")],
            FinishReason = ChatFinishReason.Stop,
        };
    }
}
