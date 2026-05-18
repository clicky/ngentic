namespace Ngentic;

/// <summary>
/// Lightweight builder for the <c>Dictionary&lt;string, object?&gt;</c> argument
/// bag that MCP client APIs typically take. Lets tests write
/// <c>Args.Of(("slot", slotA), ("script", "..."))</c> instead of a full
/// dictionary initializer.
/// </summary>
public static class Args
{
    public static Dictionary<string, object?> Of(params (string name, object? value)[] pairs)
    {
        Dictionary<string, object?> result = new(pairs.Length);
        foreach ((string name, object? value) in pairs)
        {
            result[name] = value;
        }
        return result;
    }
}
