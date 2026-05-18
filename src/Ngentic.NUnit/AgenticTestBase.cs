using System.Reflection;
using NUnit.Framework;

namespace Ngentic.NUnit;

/// <summary>
/// Base class for tests that drive an MCP-enabled Claude session via the
/// local <c>claude</c> CLI. Subclasses implement <see cref="ConfigureHarness"/>
/// to register the MCP servers their tests depend on. Per-test
/// <c>[MaxTurns]</c> / <c>[Model]</c> attributes are honoured automatically.
/// </summary>
public abstract class AgenticTestBase
{
    private readonly List<McpServerSpec> _mcpServers = new();
    private string? _defaultModel;
    private double _defaultMaxBudgetUsd = 0.50;
    private int? _currentMaxTurns;
    private string? _currentModel;

    protected AgentBuilder Agent => new AgentBuilder(
        _mcpServers,
        _defaultModel,
        _defaultMaxBudgetUsd,
        _currentMaxTurns,
        _currentModel);

    /// <summary>
    /// Register an MCP server that the agent should be able to talk to. Called
    /// from <see cref="ConfigureHarness"/>.
    /// </summary>
    protected void UseMcp(
        string name,
        string command,
        IReadOnlyList<string>? args = null,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        _mcpServers.Add(new McpServerSpec(name, command, args, env));
    }

    /// <summary>Set default model + budget for every agent run in this fixture.</summary>
    protected void UseDefaults(string? model = null, double maxBudgetUsd = 0.50)
    {
        _defaultModel = model;
        _defaultMaxBudgetUsd = maxBudgetUsd;
    }

    [OneTimeSetUp]
    public void __NgenticOneTimeSetUp()
    {
        ConfigureHarness();
        VerifyMcpDependencies();
    }

    [SetUp]
    public void __NgenticSetUp()
    {
        // Surface [MaxTurns] / [Model] from the test method so the attributes
        // are load-bearing instead of decorative.
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
        HashSet<string> registered = new(_mcpServers.Select(s => s.Name), StringComparer.Ordinal);
        foreach (McpDependencyAttribute dep in GetType().GetCustomAttributes<McpDependencyAttribute>())
        {
            if (dep.Required && !registered.Contains(dep.Name))
            {
                Assert.Fail($"Required MCP dependency '{dep.Name}' was not registered. " +
                            $"Call UseMcp(\"{dep.Name}\", ...) inside {GetType().Name}.ConfigureHarness().");
            }
        }
    }
}

public sealed class AgentBuilder
{
    private readonly IReadOnlyList<McpServerSpec> _mcpServers;
    private readonly double _defaultMaxBudgetUsd;
    private readonly string? _defaultModel;
    private string? _systemPromptAppend;
    private string? _model;
    private double? _maxBudgetUsd;
    private readonly List<string> _allowedTools = new();
    // _maxTurns is preserved for [MaxTurns] reporting but the CLI uses --max-budget-usd
    // rather than a hard turn cap; it gets stashed into AgentRun.Metadata.
    private readonly int? _maxTurns;

    internal AgentBuilder(
        IReadOnlyList<McpServerSpec> mcpServers,
        string? defaultModel,
        double defaultMaxBudgetUsd,
        int? maxTurnsFromAttribute,
        string? modelFromAttribute)
    {
        _mcpServers = mcpServers;
        _defaultModel = defaultModel;
        _defaultMaxBudgetUsd = defaultMaxBudgetUsd;
        _maxTurns = maxTurnsFromAttribute;
        _model = modelFromAttribute;
    }

    public AgentBuilder WithSystemPrompt(string prompt)
    {
        _systemPromptAppend = prompt;
        return this;
    }

    public AgentBuilder WithModel(string modelId)
    {
        _model = modelId;
        return this;
    }

    public AgentBuilder WithMaxBudgetUsd(double usd)
    {
        _maxBudgetUsd = usd;
        return this;
    }

    public AgentBuilder WithAllowedTools(params string[] patterns)
    {
        _allowedTools.AddRange(patterns);
        return this;
    }

    public async Task<AgentRun> RunAsync(string prompt, CancellationToken ct = default)
    {
        ClaudeAgentRequest request = new(
            Prompt: prompt,
            McpServers: _mcpServers,
            AllowedTools: _allowedTools,
            SystemPromptAppend: _systemPromptAppend,
            Model: _model ?? _defaultModel,
            MaxBudgetUsd: _maxBudgetUsd ?? _defaultMaxBudgetUsd);

        AgentRun run = await ClaudeAgent.RunAsync(request, ct).ConfigureAwait(false);

        if (_maxTurns is not null)
        {
            Dictionary<string, object?> meta = new(run.Metadata ?? new Dictionary<string, object?>())
            {
                ["max_turns_attribute"] = _maxTurns.Value,
            };
            run = new AgentRun
            {
                Prompt = run.Prompt,
                ToolCalls = run.ToolCalls,
                FinalOutput = run.FinalOutput,
                Duration = run.Duration,
                Turns = run.Turns,
                HitTurnLimit = run.Turns > _maxTurns.Value,
                CostUsd = run.CostUsd,
                Metadata = meta,
            };
        }
        return run;
    }
}
