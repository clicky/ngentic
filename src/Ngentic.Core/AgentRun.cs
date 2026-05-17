namespace Ngentic;

public sealed class AgentRun
{
    public required string Prompt { get; init; }
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }
    public required string FinalOutput { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int Turns { get; init; }
    public required bool HitTurnLimit { get; init; }
}
