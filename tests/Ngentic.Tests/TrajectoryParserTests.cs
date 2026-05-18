using Ngentic.NUnit;
using NUnit.Framework;

namespace Ngentic.Tests;

/// <summary>
/// Exercises <see cref="TrajectoryParser"/> against captured stream-json
/// transcripts. No CLI process required — just feed in the lines and assert.
/// </summary>
[TestFixture]
public sealed class TrajectoryParserTests
{
    [Test]
    public void Parses_a_single_tool_call_and_result()
    {
        string[] lines =
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"mcp__rhino__close_slot","input":{"slot_id":"abc"}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"{\"closed\":false,\"error\":\"slot_not_found\"}"}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"slot_not_found, as you said."}]}}""",
            """{"type":"result","total_cost_usd":0.0123,"is_error":false}""",
        };

        AgentRun run = TrajectoryParser.Parse(
            prompt: "Close slot abc",
            stdoutLines: lines,
            stderr: "",
            exitCode: 0,
            duration: TimeSpan.FromSeconds(2));

        Assert.That(run.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(run.ToolCalls[0].Name, Is.EqualTo("mcp__rhino__close_slot"));
        Assert.That(run.ToolCalls[0].CallId, Is.EqualTo("toolu_1"));
        Assert.That(run.ToolCalls[0].Result, Does.Contain("slot_not_found"));
        Assert.That(run.FinalOutput, Does.Contain("slot_not_found"));
        Assert.That(run.CostUsd, Is.EqualTo(0.0123).Within(1e-9));
        Assert.That(run.Turns, Is.EqualTo(2));
    }

    [Test]
    public void Pairs_call_and_result_by_tool_use_id()
    {
        string[] lines =
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"a","name":"foo","input":{}}]}}""",
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"b","name":"foo","input":{}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"b","content":"second"}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"a","content":"first"}]}}""",
            """{"type":"result","is_error":false}""",
        };

        AgentRun run = TrajectoryParser.Parse("t", lines, "", 0, TimeSpan.Zero);

        Assert.That(run.ToolCalls, Has.Count.EqualTo(2));
        Assert.That(run.ToolCalls[0].CallId, Is.EqualTo("a"));
        Assert.That(run.ToolCalls[0].Result, Is.EqualTo("first"));
        Assert.That(run.ToolCalls[1].CallId, Is.EqualTo("b"));
        Assert.That(run.ToolCalls[1].Result, Is.EqualTo("second"));
    }

    [Test]
    public void Handles_array_content_in_tool_results()
    {
        string[] lines =
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"x","name":"foo","input":{}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"x","content":[{"type":"text","text":"part 1"},{"type":"text","text":" part 2"}]}]}}""",
            """{"type":"result"}""",
        };

        AgentRun run = TrajectoryParser.Parse("t", lines, "", 0, TimeSpan.Zero);

        Assert.That(run.ToolCalls[0].Result, Is.EqualTo("part 1 part 2"));
    }

    [Test]
    public void Marks_tool_errors_via_is_error_flag()
    {
        string[] lines =
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"x","name":"foo","input":{}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"x","content":"boom","is_error":true}]}}""",
            """{"type":"result"}""",
        };

        AgentRun run = TrajectoryParser.Parse("t", lines, "", 0, TimeSpan.Zero);

        Assert.That(run.ToolCalls[0].IsError, Is.True);
    }

    [Test]
    public void Surfaces_stderr_and_exit_code_in_metadata()
    {
        AgentRun run = TrajectoryParser.Parse(
            prompt: "x",
            stdoutLines: Array.Empty<string>(),
            stderr: "warning: foo",
            exitCode: 7,
            duration: TimeSpan.Zero);

        Assert.That(run.Metadata, Is.Not.Null);
        Assert.That(run.Metadata!["exit_code"], Is.EqualTo(7));
        Assert.That(run.Metadata!["stderr"], Is.EqualTo("warning: foo"));
    }

    [Test]
    public void Ignores_non_json_lines()
    {
        string[] lines =
        {
            "this is not json",
            "",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"hi"}]}}""",
            "garbage",
            """{"type":"result","total_cost_usd":0.01}""",
        };

        AgentRun run = TrajectoryParser.Parse("t", lines, "", 0, TimeSpan.Zero);

        Assert.That(run.FinalOutput, Is.EqualTo("hi"));
        Assert.That(run.CostUsd, Is.EqualTo(0.01).Within(1e-9));
    }

    [Test]
    public void Captures_tool_call_arguments_as_JsonElement()
    {
        string[] lines =
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"x","name":"add","input":{"a":2,"b":3}}]}}""",
            """{"type":"result"}""",
        };

        AgentRun run = TrajectoryParser.Parse("t", lines, "", 0, TimeSpan.Zero);

        Assert.That(run.ToolCalls[0].Arguments.GetProperty("a").GetInt32(), Is.EqualTo(2));
        Assert.That(run.ToolCalls[0].Arguments.GetProperty("b").GetInt32(), Is.EqualTo(3));
    }
}
