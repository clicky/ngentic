using System.Text;
using System.Text.Json;
using NUnit.Framework.Constraints;

namespace Ngentic.NUnit;

/// <summary>
/// Entry point for JSON-aware constraints. Use with NUnit's Assert.That:
/// <code>
/// Assert.That(json, Json.HasProperty("error", Is.EqualTo("slot_not_found")));
/// Assert.That(json, Json.HasProperty("slotId", Is.Not.Empty));
/// Assert.That(json, Json.IsArrayOfLength(0));
/// </code>
/// Accepts either a raw JSON string or a <see cref="JsonElement"/>. If the
/// payload is an MCP content envelope (<c>{"content":[{"type":"text","text":"..."}]}</c>)
/// the inner text is auto-unwrapped, so callers never have to walk
/// <c>content[0].text</c> by hand.
/// </summary>
public static class Json
{
    public static JsonHasPropertyConstraint HasProperty(string name) => new(name, inner: null);

    public static JsonHasPropertyConstraint HasProperty(string name, IResolveConstraint inner)
        => new(name, inner);

    public static JsonArrayLengthConstraint IsArrayOfLength(int length) => new(length);
}

public sealed class JsonHasPropertyConstraint : Constraint
{
    private readonly string _name;
    private readonly IResolveConstraint? _inner;

    public JsonHasPropertyConstraint(string name, IResolveConstraint? inner)
    {
        _name = name;
        _inner = inner;
    }

    public override string Description => _inner is null
        ? $"JSON has property '{_name}'"
        : $"JSON property '{_name}' {_inner.Resolve().Description}";

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        if (!JsonHelpers.TryGetElement(actual, out JsonElement root))
        {
            return new ConstraintResult(this, actual, ConstraintStatus.Error);
        }

        JsonElement unwrapped = JsonHelpers.UnwrapMcpEnvelope(root);

        if (unwrapped.ValueKind != JsonValueKind.Object
            || !unwrapped.TryGetProperty(_name, out JsonElement value))
        {
            return new JsonPropertyMissingResult(this, unwrapped, _name);
        }

        if (_inner is null)
        {
            return new JsonPropertyFoundResult(this, unwrapped, _name, value);
        }

        object? clr = JsonHelpers.ToClr(value);
        IConstraint resolved = _inner.Resolve();
        ConstraintResult innerResult = resolved.ApplyTo(clr);
        return new JsonPropertyInnerResult(this, unwrapped, _name, value, innerResult);
    }
}

public sealed class JsonArrayLengthConstraint : Constraint
{
    private readonly int _expected;

    public JsonArrayLengthConstraint(int expected)
    {
        _expected = expected;
    }

    public override string Description => $"JSON array of length {_expected}";

    public override ConstraintResult ApplyTo<TActual>(TActual actual)
    {
        if (!JsonHelpers.TryGetElement(actual, out JsonElement root))
        {
            return new ConstraintResult(this, actual, ConstraintStatus.Error);
        }

        JsonElement unwrapped = JsonHelpers.UnwrapMcpEnvelope(root);

        if (unwrapped.ValueKind != JsonValueKind.Array)
        {
            return new ConstraintResult(this, $"{unwrapped.ValueKind}: {unwrapped.GetRawText()}", false);
        }

        int actualLength = unwrapped.GetArrayLength();
        return new ConstraintResult(
            this,
            $"array of length {actualLength}: {unwrapped.GetRawText()}",
            actualLength == _expected);
    }
}

internal static class JsonHelpers
{
    public static bool TryGetElement(object? actual, out JsonElement element)
    {
        switch (actual)
        {
            case JsonElement e:
                element = e;
                return true;
            case string s:
                try
                {
                    element = JsonDocument.Parse(s).RootElement;
                    return true;
                }
                catch (JsonException)
                {
                    element = default;
                    return false;
                }
            default:
                element = default;
                return false;
        }
    }

    // Recognises the MCP tool-result envelope shape and digs out the inner
    // payload. The envelope wraps a JSON-encoded string inside a content
    // block: {"content":[{"type":"text","text":"{...}"}]}. We concatenate
    // the text fields (some tools split into multiple blocks) and try to
    // parse the result as JSON; if parsing fails we leave the envelope
    // alone, so non-JSON text responses don't get mangled. Recurses to
    // handle nested envelopes if a server ever produces them.
    public static JsonElement UnwrapMcpEnvelope(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }
        if (!element.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array
            || content.GetArrayLength() == 0)
        {
            return element;
        }

        StringBuilder sb = new();
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("text", out JsonElement text)
                || text.ValueKind != JsonValueKind.String)
            {
                return element;
            }
            sb.Append(text.GetString());
        }

        try
        {
            JsonElement inner = JsonDocument.Parse(sb.ToString()).RootElement;
            return UnwrapMcpEnvelope(inner);
        }
        catch (JsonException)
        {
            return element;
        }
    }

    public static object? ToClr(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.TryGetInt64(out long l) ? l : value.GetDouble(),
            _ => value,
        };
    }
}

internal sealed class JsonPropertyMissingResult : ConstraintResult
{
    private readonly JsonElement _root;
    private readonly string _name;

    public JsonPropertyMissingResult(IConstraint constraint, JsonElement root, string name)
        : base(constraint, "property missing", isSuccess: false)
    {
        _root = root;
        _name = name;
    }

    public override void WriteMessageTo(MessageWriter writer)
    {
        writer.WriteLine($"  Expected: {Description}");
        writer.WriteLine($"  But was:  property '{_name}' not found");
        writer.WriteLine($"  JSON:     {_root.GetRawText()}");
    }
}

internal sealed class JsonPropertyFoundResult : ConstraintResult
{
    public JsonPropertyFoundResult(IConstraint constraint, JsonElement root, string name, JsonElement value)
        : base(constraint, value.GetRawText(), isSuccess: true)
    {
        _ = root;
        _ = name;
    }
}

internal sealed class JsonPropertyInnerResult : ConstraintResult
{
    private readonly JsonElement _root;
    private readonly string _name;
    private readonly JsonElement _propertyValue;
    private readonly ConstraintResult _inner;

    public JsonPropertyInnerResult(
        IConstraint constraint,
        JsonElement root,
        string name,
        JsonElement propertyValue,
        ConstraintResult inner)
        : base(constraint, inner.ActualValue, inner.IsSuccess)
    {
        _root = root;
        _name = name;
        _propertyValue = propertyValue;
        _inner = inner;
    }

    public override void WriteMessageTo(MessageWriter writer)
    {
        writer.WriteLine($"  Expected: {Description}");
        writer.WriteLine($"  But was:  property '{_name}' = {_propertyValue.GetRawText()}");
        writer.WriteLine($"  JSON:     {_root.GetRawText()}");
    }
}
