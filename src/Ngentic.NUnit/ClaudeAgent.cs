using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ngentic.NUnit;

/// <summary>
/// Drives a real Claude session via the local <c>claude</c> CLI in headless
/// mode (<c>-p --output-format stream-json</c>). The CLI handles MCP wiring
/// itself — we hand it an inline <c>--mcp-config</c> built from the supplied
/// <see cref="McpServerSpec"/>s. Tests that depend on this should be
/// <c>[Explicit]</c>: real LLM calls cost subscription quota and are
/// non-deterministic.
/// </summary>
public static class ClaudeAgent
{
    public static async Task<AgentRun> RunAsync(
        ClaudeAgentRequest request,
        CancellationToken ct = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        string mcpConfigJson = BuildMcpConfig(request.McpServers);
        ProcessStartInfo psi = new()
        {
            FileName = ResolveClaudeCli(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(request.Prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add(mcpConfigJson);
        psi.ArgumentList.Add("--permission-mode");
        psi.ArgumentList.Add("bypassPermissions");
        psi.ArgumentList.Add("--max-budget-usd");
        psi.ArgumentList.Add(request.MaxBudgetUsd.ToString("0.00", CultureInfo.InvariantCulture));
        psi.ArgumentList.Add("--no-session-persistence");
        if (request.AllowedTools.Count > 0)
        {
            psi.ArgumentList.Add("--allowedTools");
            psi.ArgumentList.Add(string.Join(" ", request.AllowedTools));
        }
        // Setting --tools shrinks the built-in tool surface so deferred-tool
        // mode (ToolSearch) doesn't kick in. Pass "" to disable all builtins;
        // tests that only need MCP tools should do this to keep trajectories
        // deterministic.
        if (request.BuiltinTools is not null)
        {
            psi.ArgumentList.Add("--tools");
            psi.ArgumentList.Add(request.BuiltinTools);
        }
        if (request.SystemPromptAppend is not null)
        {
            psi.ArgumentList.Add("--append-system-prompt");
            psi.ArgumentList.Add(request.SystemPromptAppend);
        }
        if (request.Model is not null)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(request.Model);
        }

        using Process proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start `claude` CLI.");

        // Pipes can deadlock if either stream fills its buffer while we wait
        // for the process — drain both concurrently.
        Task<List<string>> stdoutTask = ReadLinesAsync(proc.StandardOutput, ct);
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        List<string> stdoutLines = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);

        stopwatch.Stop();

        return TrajectoryParser.Parse(
            request.Prompt,
            stdoutLines,
            stderr,
            proc.ExitCode,
            stopwatch.Elapsed);
    }

    private static string BuildMcpConfig(IReadOnlyList<McpServerSpec> servers)
    {
        Dictionary<string, object?> map = new();
        foreach (McpServerSpec spec in servers)
        {
            map[spec.Name] = new Dictionary<string, object?>
            {
                ["command"] = spec.Command,
                ["args"] = spec.Args ?? (IReadOnlyList<string>)Array.Empty<string>(),
                ["env"] = spec.Env,
            };
        }
        return JsonSerializer.Serialize(new { mcpServers = map });
    }

    private static string ResolveClaudeCli()
    {
        string? overrideExe = Environment.GetEnvironmentVariable("CLAUDE_CLI");
        if (!string.IsNullOrEmpty(overrideExe))
        {
            return overrideExe;
        }
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "claude.exe" : "claude";
    }

    private static async Task<List<string>> ReadLinesAsync(StreamReader reader, CancellationToken ct)
    {
        List<string> lines = new();
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            lines.Add(line);
        }
        return lines;
    }
}

public sealed record ClaudeAgentRequest(
    string Prompt,
    IReadOnlyList<McpServerSpec> McpServers,
    IReadOnlyList<string> AllowedTools,
    string? SystemPromptAppend = null,
    string? Model = null,
    double MaxBudgetUsd = 0.50,
    string? WorkingDirectory = null,
    string? BuiltinTools = null);
