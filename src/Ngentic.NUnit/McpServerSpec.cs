namespace Ngentic.NUnit;

/// <summary>
/// Declarative description of an MCP server to hand to the local Claude CLI
/// via <c>--mcp-config</c>. Consumer code (e.g. a rhino-specific test) builds
/// these in their fixture setup and passes them to <see cref="ClaudeAgent"/>.
/// </summary>
public sealed record McpServerSpec(
    string Name,
    string Command,
    IReadOnlyList<string>? Args = null,
    IReadOnlyDictionary<string, string?>? Env = null);
