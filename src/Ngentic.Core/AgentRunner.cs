using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Ngentic;

public sealed class AgentRunner
{
    private readonly IChatClient _client;
    private readonly IList<AITool> _tools;
    private readonly string? _systemPrompt;
    private readonly int _maxTurns;

    public AgentRunner(
        IChatClient client,
        IList<AITool> tools,
        string? systemPrompt = null,
        int maxTurns = 16)
    {
        _client = client;
        _tools = tools;
        _systemPrompt = systemPrompt;
        _maxTurns = maxTurns;
    }

    public async Task<AgentRun> RunAsync(string prompt, CancellationToken ct = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<ChatMessage> messages = new();
        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, _systemPrompt));
        }
        messages.Add(new ChatMessage(ChatRole.User, prompt));

        List<ToolCall> toolCalls = new();
        ChatOptions options = new()
        {
            Tools = _tools,
            ToolMode = ChatToolMode.Auto,
        };

        StringBuilder finalText = new();
        int turn = 0;
        bool hitLimit = false;

        while (turn < _maxTurns)
        {
            turn++;
            ChatResponse response = await _client.GetResponseAsync(messages, options, ct);
            foreach (ChatMessage msg in response.Messages)
            {
                messages.Add(msg);
                foreach (AIContent content in msg.Contents)
                {
                    switch (content)
                    {
                        case FunctionCallContent call:
                            // Tool invocation is handled by FunctionInvokingChatClient downstream;
                            // we record the call shape here. Result is filled below.
                            toolCalls.Add(new ToolCall
                            {
                                Name = call.Name,
                                Arguments = SerializeArgs(call.Arguments),
                                Result = "",
                                IsError = false,
                            });
                            break;
                        case FunctionResultContent result:
                            int idx = toolCalls.FindLastIndex(t => t.Result == "" && t.Name != "");
                            if (idx >= 0)
                            {
                                toolCalls[idx] = new ToolCall
                                {
                                    Name = toolCalls[idx].Name,
                                    Arguments = toolCalls[idx].Arguments,
                                    Result = result.Result?.ToString() ?? "",
                                    IsError = result.Exception != null,
                                };
                            }
                            break;
                        case TextContent text:
                            finalText.Append(text.Text);
                            break;
                    }
                }
            }

            bool moreWork = response.Messages
                .SelectMany(m => m.Contents)
                .Any(c => c is FunctionCallContent);

            if (!moreWork)
            {
                break;
            }
            if (turn >= _maxTurns)
            {
                hitLimit = true;
            }
        }

        stopwatch.Stop();
        return new AgentRun
        {
            Prompt = prompt,
            ToolCalls = toolCalls,
            FinalOutput = finalText.ToString(),
            Duration = stopwatch.Elapsed,
            Turns = turn,
            HitTurnLimit = hitLimit,
        };
    }

    private static JsonElement SerializeArgs(IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0)
        {
            return JsonDocument.Parse("{}").RootElement;
        }
        string json = JsonSerializer.Serialize(args);
        return JsonDocument.Parse(json).RootElement;
    }
}
