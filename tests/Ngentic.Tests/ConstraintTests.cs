using Ngentic.NUnit;
using NUnit.Framework;

namespace Ngentic.Tests;

[TestFixture]
public sealed class ConstraintTests
{
    private static void AssertFails(Action test)
    {
        Assert.Throws<AssertionException>(test);
    }

    [Test]
    public void CallTool_passes_when_tool_was_called()
    {
        AgentRun run = TestHelpers.MakeRun(("foo", null));
        Assert.That(run, Did.CallTool("foo"));
    }

    [Test]
    public void CallTool_fails_when_tool_was_not_called()
    {
        AgentRun run = TestHelpers.MakeRun(("bar", null));
        AssertFails(() => Assert.That(run, Did.CallTool("foo")));
    }

    [Test]
    public void CallTool_supports_wildcard_suffix()
    {
        AgentRun run = TestHelpers.MakeRun(("mcp__rhino__spawn_slot", null));
        Assert.That(run, Did.CallTool("mcp__rhino__*"));
    }

    [Test]
    public void CallTool_WithArg_matches_int_argument()
    {
        AgentRun run = TestHelpers.MakeRun(("add", new { a = 2, b = 3 }));
        Assert.That(run, Did.CallTool("add").WithArg("a", 2).WithArg("b", 3));
    }

    [Test]
    public void CallTool_WithArg_fails_on_wrong_value()
    {
        AgentRun run = TestHelpers.MakeRun(("add", new { a = 2, b = 3 }));
        AssertFails(() => Assert.That(run, Did.CallTool("add").WithArg("a", 99)));
    }

    [Test]
    public void CallTool_Times_counts_invocations()
    {
        AgentRun run = TestHelpers.MakeRun(("foo", null), ("foo", null), ("bar", null));
        Assert.That(run, Did.CallTool("foo").Times(2));
    }

    [Test]
    public void NotCallTool_passes_when_tool_absent()
    {
        AgentRun run = TestHelpers.MakeRun(("foo", null));
        Assert.That(run, Did.NotCallTool("bar"));
    }

    [Test]
    public void NotCallTool_fails_when_tool_present()
    {
        AgentRun run = TestHelpers.MakeRun(("foo", null));
        AssertFails(() => Assert.That(run, Did.NotCallTool("foo")));
    }

    [Test]
    public void CallToolsInOrder_matches_subsequence()
    {
        AgentRun run = TestHelpers.MakeRun(("a", null), ("b", null), ("c", null), ("d", null));
        Assert.That(run, Did.CallToolsInOrder("a", "c"));
    }

    [Test]
    public void CallToolsInOrder_fails_when_out_of_order()
    {
        AgentRun run = TestHelpers.MakeRun(("c", null), ("a", null));
        AssertFails(() => Assert.That(run, Did.CallToolsInOrder("a", "c")));
    }

    [Test]
    public void MakeToolCalls_Exactly_counts_total()
    {
        AgentRun run = TestHelpers.MakeRun(("a", null), ("b", null));
        Assert.That(run, Did.MakeToolCalls.Exactly(2));
    }

    [Test]
    public void MakeToolCalls_AtMost_caps_count()
    {
        AgentRun run = TestHelpers.MakeRun(("a", null), ("b", null));
        Assert.That(run, Did.MakeToolCalls.AtMost(5));
        AssertFails(() => Assert.That(run, Did.MakeToolCalls.AtMost(1)));
    }

    [Test]
    public void Constraint_failure_shows_actual_trajectory_in_message()
    {
        AgentRun run = TestHelpers.MakeRun(("actual_tool", null));
        AssertionException ex = Assert.Throws<AssertionException>(
            (Action)(() => Assert.That(run, Did.CallTool("missing_tool"))))!;
        Assert.That(ex.Message, Does.Contain("actual_tool"));
        Assert.That(ex.Message, Does.Contain("missing_tool"));
    }
}
