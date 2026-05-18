using System.Text.Json;

namespace Ngentic;

public sealed class ToolCall
{
    public string CallId { get; init; } = "";
    public string Name { get; init; } = "";
    public JsonElement Arguments { get; init; }
    public string Result { get; init; } = "";
    public bool IsError { get; init; }
    public TimeSpan Duration { get; init; }
}
