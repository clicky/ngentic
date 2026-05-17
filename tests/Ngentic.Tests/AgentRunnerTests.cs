using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Ngentic.Tests;

[TestFixture]
public sealed class AgentRunnerTests
{
    [Test]
    public async Task Runner_invokes_tool_and_captures_call()
    {
        int invocationCount = 0;
        AITool addTool = AIFunctionFactory.Create(
            (int a, int b) => { invocationCount++; return a + b; },
            name: "add",
            description: "Add two integers.");

        FakeChatClient client = new FakeChatClient(scriptedReplies: new AIContent[]
        {
            new FunctionCallContent("call-1", "add",
                new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 }),
        }, finalText: "answer: 5");

        AgentRunner runner = new AgentRunner(client, new List<AITool> { addTool });
        AgentRun run = await runner.RunAsync("what is 2 + 3?");

        Assert.That(invocationCount, Is.EqualTo(1), "FunctionInvokingChatClient should have invoked the tool");
        Assert.That(run.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(run.ToolCalls[0].Name, Is.EqualTo("add"));
        Assert.That(run.ToolCalls[0].Result, Is.EqualTo("5"));
        Assert.That(run.FinalOutput, Does.Contain("answer: 5"));
    }

    [Test]
    public async Task Runner_links_call_and_result_by_callId()
    {
        int counter = 0;
        AITool tool = AIFunctionFactory.Create(
            () => $"result-{++counter}",
            name: "next",
            description: "Returns sequential results.");

        FakeChatClient client = new FakeChatClient(scriptedReplies: new AIContent[]
        {
            new FunctionCallContent("call-A", "next", new Dictionary<string, object?>()),
            new FunctionCallContent("call-B", "next", new Dictionary<string, object?>()),
        }, finalText: "done");

        AgentRunner runner = new AgentRunner(client, new List<AITool> { tool });
        AgentRun run = await runner.RunAsync("call twice");

        Assert.That(run.ToolCalls, Has.Count.EqualTo(2));
        Assert.That(run.ToolCalls[0].CallId, Is.EqualTo("call-A"));
        Assert.That(run.ToolCalls[0].Result, Is.EqualTo("result-1"));
        Assert.That(run.ToolCalls[1].CallId, Is.EqualTo("call-B"));
        Assert.That(run.ToolCalls[1].Result, Is.EqualTo("result-2"));
    }

    [Test]
    public async Task Runner_propagates_model_id_to_options()
    {
        FakeChatClient client = new FakeChatClient(Array.Empty<AIContent>(), finalText: "hi");
        AgentRunner runner = new AgentRunner(client, new List<AITool>(), modelId: "claude-sonnet-4-6");
        await runner.RunAsync("hello");

        Assert.That(client.LastOptions?.ModelId, Is.EqualTo("claude-sonnet-4-6"));
    }

    [Test]
    public async Task Runner_includes_system_prompt_in_messages()
    {
        FakeChatClient client = new FakeChatClient(Array.Empty<AIContent>(), finalText: "hi");
        AgentRunner runner = new AgentRunner(client, new List<AITool>(), systemPrompt: "BE BRIEF");
        await runner.RunAsync("hello");

        IList<ChatMessage> firstCallMessages = client.MessageHistoryPerCall[0];
        Assert.That(firstCallMessages, Has.Count.EqualTo(2));
        Assert.That(firstCallMessages[0].Role, Is.EqualTo(ChatRole.System));
        Assert.That(firstCallMessages[0].Text, Is.EqualTo("BE BRIEF"));
        Assert.That(firstCallMessages[1].Role, Is.EqualTo(ChatRole.User));
    }
}
