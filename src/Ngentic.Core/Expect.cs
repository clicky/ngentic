namespace Ngentic;

public static class Expect
{
    public static TrajectoryAssertion That(AgentRun run) => new TrajectoryAssertion(run);
    public static OutputAssertion That(string finalOutput) => new OutputAssertion(finalOutput);
}

public sealed class TrajectoryAssertion
{
    private readonly AgentRun _run;
    private readonly List<Predicate<AgentRun>> _pending = new();
    private bool _orMode;

    internal TrajectoryAssertion(AgentRun run)
    {
        _run = run;
    }

    public TrajectoryAssertion CalledTool(string name)
    {
        AddClause(r => r.ToolCalls.Any(c => Matches(c.Name, name)));
        return this;
    }

    public TrajectoryAssertion DidNotCall(string name)
    {
        Flush();
        if (_run.ToolCalls.Any(c => Matches(c.Name, name)))
        {
            throw new AgenticAssertionException(
                $"Expected agent NOT to call '{name}', but it did.\n{Trace(_run)}");
        }
        return this;
    }

    public TrajectoryAssertion CalledInOrder(params string[] names)
    {
        Flush();
        int idx = 0;
        foreach (ToolCall call in _run.ToolCalls)
        {
            if (idx < names.Length && Matches(call.Name, names[idx]))
            {
                idx++;
            }
        }
        if (idx < names.Length)
        {
            throw new AgenticAssertionException(
                $"Expected tools called in order [{string.Join(", ", names)}], but order was not satisfied.\n{Trace(_run)}");
        }
        return this;
    }

    public TrajectoryAssertion Or => Pivot(true);

    public TrajectoryAssertion AtLeastOnce()
    {
        Flush();
        return this;
    }

    public TrajectoryAssertion HasCountLessThan(int limit)
    {
        Flush();
        if (_run.ToolCalls.Count >= limit)
        {
            throw new AgenticAssertionException(
                $"Expected fewer than {limit} tool calls, got {_run.ToolCalls.Count}.\n{Trace(_run)}");
        }
        return this;
    }

    public TrajectoryAssertion HasCount(int count)
    {
        Flush();
        if (_run.ToolCalls.Count != count)
        {
            throw new AgenticAssertionException(
                $"Expected exactly {count} tool calls, got {_run.ToolCalls.Count}.\n{Trace(_run)}");
        }
        return this;
    }

    private TrajectoryAssertion Pivot(bool orMode)
    {
        _orMode = orMode;
        return this;
    }

    private void AddClause(Predicate<AgentRun> clause)
    {
        if (_orMode && _pending.Count > 0)
        {
            Predicate<AgentRun> last = _pending[^1];
            _pending[^1] = r => last(r) || clause(r);
            _orMode = false;
        }
        else
        {
            _pending.Add(clause);
        }
    }

    private void Flush()
    {
        foreach (Predicate<AgentRun> clause in _pending)
        {
            if (!clause(_run))
            {
                throw new AgenticAssertionException(
                    $"Trajectory assertion failed.\n{Trace(_run)}");
            }
        }
        _pending.Clear();
    }

    private static bool Matches(string actual, string pattern)
    {
        if (pattern.EndsWith('*'))
        {
            string prefix = pattern[..^1];
            return actual.StartsWith(prefix, StringComparison.Ordinal);
        }
        return actual == pattern;
    }

    private static string Trace(AgentRun run)
    {
        if (run.ToolCalls.Count == 0)
        {
            return "  (no tool calls)";
        }
        return "  Trajectory:\n" + string.Join("\n",
            run.ToolCalls.Select((c, i) => $"    [{i}] {c.Name}"));
    }
}

public sealed class OutputAssertion
{
    private readonly string _output;
    internal OutputAssertion(string output) { _output = output; }

    public OutputAssertion Contains(string substring)
    {
        if (!_output.Contains(substring, StringComparison.OrdinalIgnoreCase))
        {
            throw new AgenticAssertionException(
                $"Expected output to contain '{substring}'. Output was:\n{_output}");
        }
        return this;
    }
}

public sealed class AgenticAssertionException : Exception
{
    public AgenticAssertionException(string message) : base(message) { }
}
