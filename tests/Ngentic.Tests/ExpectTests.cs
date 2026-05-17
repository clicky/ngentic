using NUnit.Framework;

namespace Ngentic.Tests;

/// <summary>
/// Covers the framework-agnostic Expect DSL. The constraint-based API in
/// Ngentic.NUnit is preferred for NUnit consumers, but Expect remains for
/// non-NUnit callers.
/// </summary>
[TestFixture]
public sealed class ExpectTests
{
    private static void AssertPasses(Action act) => Assert.DoesNotThrow(act);
    private static void AssertFails(Action act) => Assert.Throws<AgenticAssertionException>(act);

    [Test]
    public void CalledTool_AtLeastOnce_passes_when_present()
    {
        AgentRun run = TestHelpers.MakeRun(("foo", null));
        AssertPasses(() => Expect.That(run).CalledTool("foo").AtLeastOnce());
    }

    [Test]
    public void CalledTool_throws_when_absent()
    {
        AgentRun run = TestHelpers.MakeRun(("bar", null));
        AssertFails(() => Expect.That(run).CalledTool("foo").AtLeastOnce());
    }

    [Test]
    public void DidNotCall_throws_when_present()
    {
        AgentRun run = TestHelpers.MakeRun(("foo", null));
        AssertFails(() => Expect.That(run).DidNotCall("foo"));
    }

    [Test]
    public void HasCount_matches_exact()
    {
        AgentRun run = TestHelpers.MakeRun(("a", null), ("b", null));
        AssertPasses(() => Expect.That(run).HasCount(2));
        AssertFails(() => Expect.That(run).HasCount(3));
    }

    [Test]
    public void HasCountLessThan_enforces_upper_bound()
    {
        AgentRun run = TestHelpers.MakeRun(("a", null), ("b", null));
        AssertPasses(() => Expect.That(run).HasCountLessThan(5));
        AssertFails(() => Expect.That(run).HasCountLessThan(2));
    }

    [Test]
    public void OutputAssertion_Contains_is_case_insensitive()
    {
        AssertPasses(() => Expect.That("Hello World").Contains("hello"));
        AssertFails(() => Expect.That("Hello World").Contains("missing"));
    }
}
