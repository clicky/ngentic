using Microsoft.Extensions.AI;

namespace Ngentic.Tests;

/// <summary>
/// Test double: returns a scripted sequence of replies. Each call to GetResponseAsync
/// advances one step. Each reply is either a tool call or a final text message.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly IReadOnlyList<AIContent> _scriptedReplies;
    private readonly string _finalText;
    private int _turn;

    public int CallsObserved { get; private set; }
    public List<IList<ChatMessage>> MessageHistoryPerCall { get; } = new();
    public ChatOptions? LastOptions { get; private set; }

    public FakeChatClient(IEnumerable<AIContent> scriptedReplies, string finalText = "")
    {
        _scriptedReplies = scriptedReplies.ToList();
        _finalText = finalText;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallsObserved++;
        MessageHistoryPerCall.Add(messages.ToList());
        LastOptions = options;

        ChatMessage reply;
        if (_turn < _scriptedReplies.Count)
        {
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { _scriptedReplies[_turn] });
        }
        else
        {
            reply = new ChatMessage(ChatRole.Assistant, _finalText);
        }
        _turn++;
        return Task.FromResult(new ChatResponse(reply));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
