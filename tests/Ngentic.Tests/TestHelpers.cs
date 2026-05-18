using System.Text.Json;

namespace Ngentic.Tests;

internal static class TestHelpers
{
    public static AgentRun MakeRun(params (string name, object? args)[] calls)
    {
        List<ToolCall> toolCalls = new();
        int i = 0;
        foreach ((string name, object? args) in calls)
        {
            toolCalls.Add(new ToolCall
            {
                CallId = $"call-{i++}",
                Name = name,
                Arguments = ToJson(args),
                Result = "",
                IsError = false,
            });
        }
        return new AgentRun
        {
            Prompt = "test",
            ToolCalls = toolCalls,
            FinalOutput = "",
            Duration = TimeSpan.Zero,
            Turns = 1,
            HitTurnLimit = false,
        };
    }

    private static JsonElement ToJson(object? args)
    {
        if (args == null)
        {
            return JsonDocument.Parse("{}").RootElement;
        }
        string json = JsonSerializer.Serialize(args);
        return JsonDocument.Parse(json).RootElement;
    }
}
