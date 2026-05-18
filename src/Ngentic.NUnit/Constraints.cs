using System.Text.Json;
using NUnit.Framework.Constraints;

namespace Ngentic.NUnit;

/// <summary>
/// Entry point for trajectory constraints. Use with NUnit's Assert.That:
/// <code>Assert.That(run, Did.CallTool("mcp__rhino__spawn_slot"));</code>
/// </summary>
public static class Did
{
    public static CalledToolConstraint CallTool(string name) => new CalledToolConstraint(name);
    public static NotCalledToolConstraint NotCallTool(string name) => new NotCalledToolConstraint(name);
    public static CalledInOrderConstraint CallToolsInOrder(params string[] names) => new CalledInOrderConstraint(names);
    public static ToolCallCountConstraint MakeToolCalls => new ToolCallCountConstraint();
}

public abstract class AgentRunConstraint : Constraint
{
    private string _description = "";
    public override string Description => _description;
    protected void SetDescription(string text) => _description = text;

    protected static bool NameMatches(string actual, string pattern)
    {
        if (pattern.EndsWith('*'))
        {
            return actual.StartsWith(pattern[..^1], StringComparison.Ordinal);
        }
        return actual == pattern;
    }
}

public sealed class CalledToolConstraint : AgentRunConstraint
{
    private readonly string _pattern;
    private int _expectedCount = -1;
    private readonly List<Func<JsonElement, bool>> _argChecks = new();
    private readonly List<string> _argDescriptions = new();

    public CalledToolConstraint(string pattern)
    {
        _pattern = pattern;
        SetDescription($"agent called tool '{pattern}'");
    }

    public CalledToolConstraint AtLeastOnce()
    {
        _expectedCount = -1;
        SetDescription($"agent called tool '{_pattern}' at least once");
        return this;
    }

    public CalledToolConstraint Times(int count)
    {
        _expectedCount = count;
        SetDescription($"agent called tool '{_pattern}' exactly {count} time(s)");
        return this;
    }

    public CalledToolConstraint WithArg(string name, object expected)
    {
        _argChecks.Add(args =>
        {
            if (!args.TryGetProperty(name, out JsonElement value))
            {
                return false;
            }
            return JsonEquals(value, expected);
        });
        _argDescriptions.Add($"{name}={expected}");
        SetDescription($"agent called tool '{_pattern}' with {string.Join(", ", _argDescriptions)}");
        return this;
    }

    public CalledToolConstraint WithArgs(Func<JsonElement, bool> predicate, string? describe = null)
    {
        _argChecks.Add(predicate);
        _argDescriptions.Add(describe ?? "<predicate>");
        SetDescription($"agent called tool '{_pattern}' with {string.Join(", ", _argDescriptions)}");
        return this;
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        if (actual is not AgentRun run)
        {
            return new ConstraintResult(this, actual, ConstraintStatus.Error);
        }

        List<ToolCall> matchingFully = run.ToolCalls
            .Where(c => NameMatches(c.Name, _pattern))
            .Where(c => _argChecks.All(check => check(c.Arguments)))
            .ToList();

        bool success = _expectedCount < 0
            ? matchingFully.Count > 0
            : matchingFully.Count == _expectedCount;

        return new AgentRunConstraintResult(this, run, success);
    }

    private static bool JsonEquals(JsonElement value, object expected)
    {
        return expected switch
        {
            int i => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int v) && v == i,
            long l => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long v) && v == l,
            double d => value.ValueKind == JsonValueKind.Number && Math.Abs(value.GetDouble() - d) < 1e-9,
            bool b => value.ValueKind == (b ? JsonValueKind.True : JsonValueKind.False),
            string s => value.ValueKind == JsonValueKind.String && value.GetString() == s,
            null => value.ValueKind == JsonValueKind.Null,
            _ => value.ToString() == expected.ToString(),
        };
    }
}

public sealed class NotCalledToolConstraint : AgentRunConstraint
{
    private readonly string _pattern;

    public NotCalledToolConstraint(string pattern)
    {
        _pattern = pattern;
        SetDescription($"agent did NOT call tool '{pattern}'");
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        if (actual is not AgentRun run)
        {
            return new ConstraintResult(this, actual, ConstraintStatus.Error);
        }
        bool success = !run.ToolCalls.Any(c => NameMatches(c.Name, _pattern));
        return new AgentRunConstraintResult(this, run, success);
    }
}

public sealed class CalledInOrderConstraint : AgentRunConstraint
{
    private readonly string[] _names;

    public CalledInOrderConstraint(string[] names)
    {
        _names = names;
        SetDescription($"agent called tools in order: [{string.Join(", ", names)}]");
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        if (actual is not AgentRun run)
        {
            return new ConstraintResult(this, actual, ConstraintStatus.Error);
        }
        int idx = 0;
        foreach (ToolCall call in run.ToolCalls)
        {
            if (idx < _names.Length && NameMatches(call.Name, _names[idx]))
            {
                idx++;
            }
        }
        return new AgentRunConstraintResult(this, run, idx >= _names.Length);
    }
}

public sealed class ToolCallCountConstraint : AgentRunConstraint
{
    private int _min = -1;
    private int _max = -1;
    private int _exact = -1;

    public ToolCallCountConstraint()
    {
        SetDescription("agent made tool calls");
    }

    public ToolCallCountConstraint Exactly(int count)
    {
        _exact = count;
        SetDescription($"agent made exactly {count} tool call(s)");
        return this;
    }

    public ToolCallCountConstraint AtLeast(int count)
    {
        _min = count;
        SetDescription($"agent made at least {count} tool call(s)");
        return this;
    }

    public ToolCallCountConstraint AtMost(int count)
    {
        _max = count;
        SetDescription($"agent made at most {count} tool call(s)");
        return this;
    }

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        if (actual is not AgentRun run)
        {
            return new ConstraintResult(this, actual, ConstraintStatus.Error);
        }
        int n = run.ToolCalls.Count;
        bool ok = true;
        if (_exact >= 0) ok &= n == _exact;
        if (_min >= 0) ok &= n >= _min;
        if (_max >= 0) ok &= n <= _max;
        return new AgentRunConstraintResult(this, run, ok);
    }
}

internal sealed class AgentRunConstraintResult : ConstraintResult
{
    private readonly AgentRun _run;

    public AgentRunConstraintResult(IConstraint constraint, AgentRun run, bool success)
        : base(constraint, FormatActual(run), success)
    {
        _run = run;
    }

    private static string FormatActual(AgentRun run)
    {
        if (run.ToolCalls.Count == 0)
        {
            return "(no tool calls)";
        }
        return $"trajectory of {run.ToolCalls.Count} call(s): " +
               string.Join(", ", run.ToolCalls.Select(c => c.Name));
    }

    public override void WriteMessageTo(MessageWriter writer)
    {
        writer.WriteLine($"  Expected: {Description}");
        writer.WriteLine($"  But was:  trajectory of {_run.ToolCalls.Count} call(s)");
        for (int i = 0; i < _run.ToolCalls.Count; i++)
        {
            ToolCall c = _run.ToolCalls[i];
            writer.WriteLine($"    [{i}] {c.Name}({c.Arguments.GetRawText()})");
        }
    }
}
