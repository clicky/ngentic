using System.Text.Json;
using Ngentic.NUnit;
using NUnit.Framework;

namespace Ngentic.Tests;

[TestFixture]
public sealed class JsonConstraintTests
{
    private static void AssertFails(Action test)
    {
        Assert.Throws<AssertionException>(test);
    }

    [Test]
    public void HasProperty_passes_when_property_exists()
    {
        string json = """{"slotId":"abc-123","adopted":false}""";
        Assert.That(json, Json.HasProperty("slotId"));
    }

    [Test]
    public void HasProperty_fails_when_property_is_missing()
    {
        string json = """{"slotId":"abc-123"}""";
        AssertFails(() => Assert.That(json, Json.HasProperty("error")));
    }

    [Test]
    public void HasProperty_with_inner_passes_on_string_equality()
    {
        string json = """{"error":"rhino_not_installed","message":"version 'garbage' was not found"}""";
        Assert.That(json, Json.HasProperty("error", Is.EqualTo("rhino_not_installed")));
    }

    [Test]
    public void HasProperty_with_inner_passes_on_substring()
    {
        string json = """{"error":"rhino_not_installed","message":"version 'garbage' was not found"}""";
        Assert.That(json, Json.HasProperty("message", Does.Contain("garbage")));
    }

    [Test]
    public void HasProperty_with_inner_passes_on_non_empty()
    {
        string json = """{"slotId":"abc-123"}""";
        Assert.That(json, Json.HasProperty("slotId", Is.Not.Empty));
    }

    [Test]
    public void HasProperty_with_inner_fails_on_empty_string()
    {
        string json = """{"slotId":""}""";
        AssertFails(() => Assert.That(json, Json.HasProperty("slotId", Is.Not.Empty)));
    }

    [Test]
    public void HasProperty_with_inner_passes_on_bool_false()
    {
        string json = """{"adopted":false}""";
        Assert.That(json, Json.HasProperty("adopted", Is.False));
    }

    [Test]
    public void HasProperty_with_inner_passes_on_int_equality()
    {
        string json = """{"port":54321}""";
        Assert.That(json, Json.HasProperty("port", Is.EqualTo(54321)));
    }

    [Test]
    public void HasProperty_with_inner_fails_on_wrong_value()
    {
        string json = """{"error":"rhino_not_installed"}""";
        AssertFails(() => Assert.That(json, Json.HasProperty("error", Is.EqualTo("something_else"))));
    }

    [Test]
    public void HasProperty_accepts_JsonElement_directly()
    {
        JsonElement root = JsonDocument.Parse("""{"adopted":true}""").RootElement;
        Assert.That(root, Json.HasProperty("adopted", Is.True));
    }

    [Test]
    public void HasProperty_unwraps_mcp_content_envelope_for_inner_json()
    {
        // The plugin tools return a content envelope wrapping a JSON-encoded
        // string. The constraint should peek through it transparently.
        string envelope = """{"content":[{"type":"text","text":"{\"count\":3,\"layer\":\"Default\"}"}]}""";
        Assert.That(envelope, Json.HasProperty("count", Is.EqualTo(3)));
        Assert.That(envelope, Json.HasProperty("layer", Is.EqualTo("Default")));
    }

    [Test]
    public void HasProperty_unwraps_envelope_with_multiple_text_blocks()
    {
        // Some servers split JSON across multiple text blocks. Concatenated
        // text should still parse.
        string envelope = """
            {"content":[
                {"type":"text","text":"{\"error\":\"slot_"},
                {"type":"text","text":"not_found\"}"}
            ]}
            """;
        Assert.That(envelope, Json.HasProperty("error", Is.EqualTo("slot_not_found")));
    }

    [Test]
    public void HasProperty_does_not_unwrap_when_inner_text_is_not_json()
    {
        // A "plain string" response (e.g. close_doc's "Document closed.") must
        // not get mangled — falling back to the envelope means HasProperty for
        // the inner thing simply won't find the property.
        string envelope = """{"content":[{"type":"text","text":"Document closed."}]}""";
        Assert.That(envelope, Json.HasProperty("content"));
    }

    [Test]
    public void HasProperty_supports_nesting_via_inner_constraint()
    {
        // Nested objects: Json.HasProperty itself is an IResolveConstraint, so
        // it composes.
        string json = """{"outer":{"inner":"value"}}""";
        Assert.That(json, Json.HasProperty("outer", Json.HasProperty("inner", Is.EqualTo("value"))));
    }

    [Test]
    public void IsArrayOfLength_passes_for_empty_array()
    {
        string json = "[]";
        Assert.That(json, Json.IsArrayOfLength(0));
    }

    [Test]
    public void IsArrayOfLength_passes_for_matching_length()
    {
        string json = "[1,2,3]";
        Assert.That(json, Json.IsArrayOfLength(3));
    }

    [Test]
    public void IsArrayOfLength_fails_for_wrong_length()
    {
        string json = "[1,2]";
        AssertFails(() => Assert.That(json, Json.IsArrayOfLength(3)));
    }

    [Test]
    public void IsArrayOfLength_fails_when_root_is_not_an_array()
    {
        string json = """{"foo":"bar"}""";
        AssertFails(() => Assert.That(json, Json.IsArrayOfLength(0)));
    }
}

[TestFixture]
public sealed class ArgsTests
{
    [Test]
    public void Of_builds_dictionary_from_tuple_pairs()
    {
        Dictionary<string, object?> args = Args.Of(("slot", "abc"), ("count", 3));
        Assert.That(args["slot"], Is.EqualTo("abc"));
        Assert.That(args["count"], Is.EqualTo(3));
        Assert.That(args, Has.Count.EqualTo(2));
    }

    [Test]
    public void Of_with_no_pairs_returns_empty_dictionary()
    {
        Dictionary<string, object?> args = Args.Of();
        Assert.That(args, Is.Empty);
    }

    [Test]
    public void Of_accepts_null_values()
    {
        Dictionary<string, object?> args = Args.Of(("optional", null));
        Assert.That(args.ContainsKey("optional"), Is.True);
        Assert.That(args["optional"], Is.Null);
    }
}
