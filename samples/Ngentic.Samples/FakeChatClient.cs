using Microsoft.Extensions.AI;

namespace Ngentic.Samples;

internal sealed class FakeChatClient : IChatClient
{
    private readonly IReadOnlyList<FunctionCallContent> _scriptedCalls;
    private readonly string _finalText;
    private int _turn;

    public FakeChatClient(IEnumerable<FunctionCallContent> scriptedCalls, string finalText)
    {
        _scriptedCalls = scriptedCalls.ToList();
        _finalText = finalText;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatMessage reply;
        if (_turn < _scriptedCalls.Count)
        {
            FunctionCallContent call = _scriptedCalls[_turn];
            reply = new ChatMessage(ChatRole.Assistant, new List<AIContent> { call });
        }
        else
        {
            reply = new ChatMessage(ChatRole.Assistant, _finalText);
        }
        _turn++;
        ChatResponse response = new ChatResponse(reply);
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
