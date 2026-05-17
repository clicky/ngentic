using Microsoft.Extensions.AI;
using Ngentic;
using Ngentic.NUnit;
using NUnit.Framework;

namespace Ngentic.Samples;

[TestFixture]
[McpDependency("calculator")]
public sealed class CalculatorAgentTests : AgenticTestBase
{
    // Sample uses a fake IChatClient + a fake MCP registry so the test compiles
    // and runs without network calls. Real usage swaps these for the official
    // Anthropic IChatClient and the ModelContextProtocol client.
    protected override void ConfigureHarness()
    {
        FakeChatClient fakeClient = new FakeChatClient(scriptedCalls: new[]
        {
            new FunctionCallContent("call-1", "mcp__calculator__add",
                new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 }),
        }, finalText: "2 + 3 = 5");

        InMemoryMcpRegistry registry = new InMemoryMcpRegistry();
        registry.Register("calculator", _ => Task.FromResult<IList<AITool>>(new List<AITool>
        {
            AIFunctionFactory.Create(
                (int a, int b) => a + b,
                name: "mcp__calculator__add",
                description: "Add two integers."),
        }));

        UseClient(fakeClient);
        UseRegistry(registry);
    }

    [Test]
    [MaxTurns(4)]
    public async Task Agent_uses_calculator_to_add()
    {
        AgentRun run = await Agent
            .WithSystemPrompt("You are a math assistant.")
            .WithAllowedTools("mcp__calculator__*")
            .RunAsync("What is 2 + 3?");

        Assert.That(run, Did.CallTool("mcp__calculator__add").WithArg("a", 2).WithArg("b", 3));
        Assert.That(run, Did.NotCallTool("mcp__calculator__divide"));
        Assert.That(run, Did.MakeToolCalls.Exactly(1));
        Assert.That(run.FinalOutput, Does.Contain("5"));
    }
}
