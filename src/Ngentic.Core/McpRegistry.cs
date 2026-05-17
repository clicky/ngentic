using Microsoft.Extensions.AI;

namespace Ngentic;

public sealed record McpServerSpec(
    string Name,
    string Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string>? Env = null);

public interface IMcpRegistry
{
    bool IsConfigured(string name);
    Task<IList<AITool>> GetToolsAsync(string name, CancellationToken ct = default);
}

public sealed class InMemoryMcpRegistry : IMcpRegistry
{
    private readonly Dictionary<string, Func<CancellationToken, Task<IList<AITool>>>> _factories = new();

    public void Register(string name, Func<CancellationToken, Task<IList<AITool>>> factory)
    {
        _factories[name] = factory;
    }

    public bool IsConfigured(string name) => _factories.ContainsKey(name);

    public Task<IList<AITool>> GetToolsAsync(string name, CancellationToken ct = default)
    {
        if (!_factories.TryGetValue(name, out Func<CancellationToken, Task<IList<AITool>>>? factory))
        {
            throw new InvalidOperationException($"MCP server '{name}' is not registered.");
        }
        return factory(ct);
    }
}
