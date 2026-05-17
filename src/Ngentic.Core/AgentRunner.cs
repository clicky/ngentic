using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Ngentic;

public sealed class AgentRunner
{
    private readonly IChatClient _innerClient;
    private readonly IList<AITool> _tools;
    private readonly string? _systemPrompt;
    private readonly int _maxTurns;
    private readonly string? _modelId;

    public AgentRunner(
        IChatClient client,
        IList<AITool> tools,
        string? systemPrompt = null,
        int maxTurns = 16,
        string? modelId = null)
    {
        _innerClient = client;
        _tools = tools;
        _systemPrompt = systemPrompt;
        _maxTurns = maxTurns;
        _modelId = modelId;
    }

    public async Task<AgentRun> RunAsync(string prompt, CancellationToken ct = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Wrap the caller's IChatClient so MEAI invokes tools on the model's behalf.
        // Without this, FunctionCallContent comes back from the model but nothing
        // executes the AITool — the loop would spin until max turns with no progress.
        FunctionInvokingChatClient functionClient = new FunctionInvokingChatClient(_innerClient)
        {
            MaximumIterationsPerRequest = _maxTurns,
            AllowConcurrentInvocation = false,
        };

        List<ChatMessage> messages = new();
        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, _systemPrompt));
        }
        messages.Add(new ChatMessage(ChatRole.User, prompt));

        ChatOptions options = new()
        {
            Tools = _tools,
            ToolMode = ChatToolMode.Auto,
            ModelId = _modelId,
        };

        ChatResponse response = await functionClient.GetResponseAsync(messages, options, ct);

        List<ToolCall> toolCalls = new();
        StringBuilder finalText = new();

        foreach (ChatMessage msg in response.Messages)
        {
            foreach (AIContent content in msg.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        toolCalls.Add(new ToolCall
                        {
                            CallId = call.CallId,
                            Name = call.Name,
                            Arguments = SerializeArgs(call.Arguments),
                            Result = "",
                            IsError = false,
                        });
                        break;
                    case FunctionResultContent result:
                        int idx = toolCalls.FindIndex(t => t.CallId == result.CallId);
                        if (idx >= 0)
                        {
                            ToolCall existing = toolCalls[idx];
                            toolCalls[idx] = new ToolCall
                            {
                                CallId = existing.CallId,
                                Name = existing.Name,
                                Arguments = existing.Arguments,
                                Result = result.Result?.ToString() ?? "",
                                IsError = result.Exception != null,
                            };
                        }
                        break;
                    case TextContent text:
                        if (msg.Role == ChatRole.Assistant)
                        {
                            finalText.Append(text.Text);
                        }
                        break;
                }
            }
        }

        stopwatch.Stop();

        int turns = Math.Max(1, response.Messages.Count(m => m.Role == ChatRole.Assistant));
        bool hitLimit = turns >= _maxTurns
            && response.Messages.Any(m => m.Contents.OfType<FunctionCallContent>().Any()
                && !response.Messages.Any(r => r.Contents.OfType<FunctionResultContent>().Any()));

        return new AgentRun
        {
            Prompt = prompt,
            ToolCalls = toolCalls,
            FinalOutput = finalText.ToString(),
            Duration = stopwatch.Elapsed,
            Turns = turns,
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
