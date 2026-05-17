using System.Reflection;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Ngentic.NUnit;

public abstract class AgenticTestBase
{
    private IChatClient? _chatClient;
    private IMcpRegistry? _registry;
    private int? _currentMaxTurns;
    private string? _currentModel;

    protected AgentBuilder Agent => new AgentBuilder(
        RequireClient(),
        RequireRegistry(),
        CollectMcpNames(),
        _currentMaxTurns,
        _currentModel);

    protected void UseClient(IChatClient client) => _chatClient = client;
    protected void UseRegistry(IMcpRegistry registry) => _registry = registry;

    [OneTimeSetUp]
    public void __NgenticOneTimeSetUp()
    {
        ConfigureHarness();
        VerifyMcpDependencies();
    }

    [SetUp]
    public void __NgenticSetUp()
    {
        // Pick up [MaxTurns] / [Model] declared on the current test method so the
        // attributes I documented are actually load-bearing.
        string? methodName = TestContext.CurrentContext.Test.MethodName;
        if (methodName == null)
        {
            _currentMaxTurns = null;
            _currentModel = null;
            return;
        }

        MethodInfo? method = GetType().GetMethod(methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        _currentMaxTurns = method?.GetCustomAttribute<MaxTurnsAttribute>()?.Value;
        _currentModel = method?.GetCustomAttribute<ModelAttribute>()?.Id;
    }

    protected abstract void ConfigureHarness();

    private void VerifyMcpDependencies()
    {
        IMcpRegistry registry = RequireRegistry();
        foreach (McpDependencyAttribute dep in GetType().GetCustomAttributes<McpDependencyAttribute>())
        {
            if (dep.Required && !registry.IsConfigured(dep.Name))
            {
                Assert.Fail($"Required MCP dependency '{dep.Name}' is not configured. " +
                            $"Register it in {GetType().Name}.ConfigureHarness().");
            }
        }
    }

    private IReadOnlyList<string> CollectMcpNames()
    {
        List<string> names = new();
        foreach (McpDependencyAttribute dep in GetType().GetCustomAttributes<McpDependencyAttribute>())
        {
            names.Add(dep.Name);
        }
        return names;
    }

    private IChatClient RequireClient()
    {
        if (_chatClient == null)
        {
            throw new InvalidOperationException(
                "No IChatClient configured. Call UseClient(...) inside ConfigureHarness().");
        }
        return _chatClient;
    }

    private IMcpRegistry RequireRegistry()
    {
        if (_registry == null)
        {
            throw new InvalidOperationException(
                "No IMcpRegistry configured. Call UseRegistry(...) inside ConfigureHarness().");
        }
        return _registry;
    }
}

public sealed class AgentBuilder
{
    private readonly IChatClient _client;
    private readonly IMcpRegistry _registry;
    private readonly IReadOnlyList<string> _mcpNames;
    private string? _systemPrompt;
    private int _maxTurns;
    private string? _model;
    private readonly List<string> _allowedPatterns = new();

    internal AgentBuilder(
        IChatClient client,
        IMcpRegistry registry,
        IReadOnlyList<string> mcpNames,
        int? maxTurnsFromAttribute,
        string? modelFromAttribute)
    {
        _client = client;
        _registry = registry;
        _mcpNames = mcpNames;
        _maxTurns = maxTurnsFromAttribute ?? 16;
        _model = modelFromAttribute;
    }

    public AgentBuilder WithSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        return this;
    }

    public AgentBuilder WithMaxTurns(int turns)
    {
        _maxTurns = turns;
        return this;
    }

    public AgentBuilder WithModel(string modelId)
    {
        _model = modelId;
        return this;
    }

    public AgentBuilder WithAllowedTools(params string[] patterns)
    {
        _allowedPatterns.AddRange(patterns);
        return this;
    }

    public async Task<AgentRun> RunAsync(string prompt, CancellationToken ct = default)
    {
        List<AITool> tools = new();
        foreach (string mcpName in _mcpNames)
        {
            IList<AITool> mcpTools = await _registry.GetToolsAsync(mcpName, ct);
            foreach (AITool tool in mcpTools)
            {
                if (IsAllowed(tool.Name))
                {
                    tools.Add(tool);
                }
            }
        }

        AgentRunner runner = new AgentRunner(_client, tools, _systemPrompt, _maxTurns, _model);
        return await runner.RunAsync(prompt, ct);
    }

    private bool IsAllowed(string toolName)
    {
        if (_allowedPatterns.Count == 0)
        {
            return true;
        }
        foreach (string pattern in _allowedPatterns)
        {
            if (pattern.EndsWith('*'))
            {
                if (toolName.StartsWith(pattern[..^1], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (toolName == pattern)
            {
                return true;
            }
        }
        return false;
    }
}
