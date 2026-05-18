namespace Ngentic;

public sealed class AgentRun
{
    public required string Prompt { get; init; }
    public required IReadOnlyList<ToolCall> ToolCalls { get; init; }
    public required string FinalOutput { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int Turns { get; init; }
    public required bool HitTurnLimit { get; init; }

    /// <summary>
    /// Total cost reported by the agent driver, in USD. Populated by the
    /// Claude CLI driver from the final stream-json `result` event; null
    /// when the driver does not surface cost.
    /// </summary>
    public double? CostUsd { get; init; }

    /// <summary>
    /// Free-form payload for driver-specific information that does not earn
    /// a top-level field. Populated by the Claude CLI driver with stderr,
    /// exit code, and the raw stream-json transcript for debugging.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }
}
