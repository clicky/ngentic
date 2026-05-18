using System.Text;
using System.Text.Json;

namespace Ngentic.NUnit;

/// <summary>
/// Parses <c>claude -p --output-format stream-json</c> output into a
/// <see cref="AgentRun"/>. Exposed publicly so consumers can replay captured
/// transcripts in unit tests without spawning the CLI.
/// </summary>
public static class TrajectoryParser
{
    public static AgentRun Parse(
        string prompt,
        IReadOnlyList<string> stdoutLines,
        string stderr,
        int exitCode,
        TimeSpan duration)
    {
        List<ToolCall> calls = new();
        Dictionary<string, int> indexByCallId = new();
        StringBuilder finalText = new();
        int assistantMessages = 0;
        bool isError = false;
        string? errorPayload = null;
        double? costUsd = null;

        foreach (string line in stdoutLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("type", out JsonElement typeEl))
                {
                    continue;
                }
                switch (typeEl.GetString())
                {
                    case "assistant":
                        assistantMessages++;
                        ExtractAssistantContent(root, calls, indexByCallId, finalText);
                        break;
                    case "user":
                        ExtractToolResults(root, calls, indexByCallId);
                        break;
                    case "result":
                        if (root.TryGetProperty("is_error", out JsonElement errEl)
                            && errEl.ValueKind == JsonValueKind.True)
                        {
                            isError = true;
                            errorPayload = root.TryGetProperty("result", out JsonElement r)
                                ? r.GetString()
                                : null;
                        }
                        if (root.TryGetProperty("total_cost_usd", out JsonElement costEl)
                            && costEl.TryGetDouble(out double c))
                        {
                            costUsd = c;
                        }
                        break;
                }
            }
        }

        Dictionary<string, object?> metadata = new()
        {
            ["exit_code"] = exitCode,
            ["stderr"] = stderr,
            ["raw_stdout_lines"] = stdoutLines,
            ["is_error"] = isError,
            ["error_payload"] = errorPayload,
        };

        return new AgentRun
        {
            Prompt = prompt,
            ToolCalls = calls,
            FinalOutput = finalText.ToString(),
            Duration = duration,
            Turns = Math.Max(1, assistantMessages),
            HitTurnLimit = false,
            CostUsd = costUsd,
            Metadata = metadata,
        };
    }

    private static void ExtractAssistantContent(
        JsonElement root,
        List<ToolCall> calls,
        Dictionary<string, int> indexByCallId,
        StringBuilder finalText)
    {
        if (!root.TryGetProperty("message", out JsonElement msg)
            || !msg.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out JsonElement blockTypeEl))
            {
                continue;
            }
            switch (blockTypeEl.GetString())
            {
                case "text":
                    if (block.TryGetProperty("text", out JsonElement textEl))
                    {
                        finalText.Append(textEl.GetString());
                    }
                    break;
                case "tool_use":
                    string? id = block.TryGetProperty("id", out JsonElement idEl)
                        ? idEl.GetString()
                        : null;
                    string? name = block.TryGetProperty("name", out JsonElement nameEl)
                        ? nameEl.GetString()
                        : null;
                    JsonElement input = block.TryGetProperty("input", out JsonElement inEl)
                        ? inEl.Clone()
                        : JsonDocument.Parse("{}").RootElement;
                    if (name is null)
                    {
                        break;
                    }
                    string callId = id ?? Guid.NewGuid().ToString("N");
                    indexByCallId[callId] = calls.Count;
                    calls.Add(new ToolCall
                    {
                        CallId = callId,
                        Name = name,
                        Arguments = input,
                        Result = "",
                        IsError = false,
                    });
                    break;
            }
        }
    }

    private static void ExtractToolResults(
        JsonElement root,
        List<ToolCall> calls,
        Dictionary<string, int> indexByCallId)
    {
        if (!root.TryGetProperty("message", out JsonElement msg)
            || !msg.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out JsonElement blockTypeEl)
                || blockTypeEl.GetString() != "tool_result")
            {
                continue;
            }

            string? id = block.TryGetProperty("tool_use_id", out JsonElement idEl)
                ? idEl.GetString()
                : null;
            if (id is null || !indexByCallId.TryGetValue(id, out int idx))
            {
                continue;
            }

            string resultText = ExtractToolResultText(block);
            bool isErr = block.TryGetProperty("is_error", out JsonElement errEl)
                && errEl.ValueKind == JsonValueKind.True;

            ToolCall prev = calls[idx];
            calls[idx] = new ToolCall
            {
                CallId = prev.CallId,
                Name = prev.Name,
                Arguments = prev.Arguments,
                Result = resultText,
                IsError = isErr,
            };
        }
    }

    private static string ExtractToolResultText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out JsonElement contentEl))
        {
            return "";
        }
        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString() ?? "";
        }
        if (contentEl.ValueKind != JsonValueKind.Array)
        {
            return contentEl.GetRawText();
        }
        StringBuilder sb = new();
        foreach (JsonElement c in contentEl.EnumerateArray())
        {
            if (c.ValueKind == JsonValueKind.String)
            {
                sb.Append(c.GetString());
            }
            else if (c.TryGetProperty("text", out JsonElement t))
            {
                sb.Append(t.GetString());
            }
        }
        return sb.ToString();
    }
}
