using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessGrepTool : AgentWorkspaceFileAccessToolComponent
{
    private const string GrepContentOutputMode = "content";
    private const string GrepFilesWithMatchesOutputMode = "files_with_matches";
    private const string GrepCountOutputMode = "count";
    private const int DefaultGrepHeadLimit = 250;

    private readonly AITool tool;

    public AgentWorkspaceFileAccessGrepTool(AgentWorkspaceFileAccessContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(GrepAsync);
    }

    public override AITool Tool { get => tool; }

    [DisplayName(AgentWorkspaceFileAccessToolNames.Grep)]
    [Description("""
        Content search built on ripgrep. Prefer this over `grep`/`rg` - results integrate with the permission UI and file links.

        - Full regex syntax (e.g. "log.*Error", "function\s+\w+").
        - Filter with `glob` (e.g. "**/*.tsx") or `type` (e.g. "js", "py", "rust").
        - `output_mode`: "content" (matching lines), "files_with_matches" (paths only, default), or "count".
        - `multiline: true` for patterns that span lines.
        """)]
    private async Task<List<string>> GrepAsync([Description("Absolute file or directory to search. Defaults to current working directory.")][DefaultValue(null)] string? path, [Description("The regular expression pattern to search for in file contents")] string pattern, [Description("Case insensitive search")][DefaultValue(false)] bool ignore_case, [Description("Enable multiline mode where . matches newlines and patterns can span lines. Default: false.")][DefaultValue(false)] bool multiline, [Description("""Show line numbers in output. Requires output_mode: "content", ignored otherwise. Defaults to true.""")][DefaultValue(true)] bool show_line_numbers, [Description("""Print only the matched (non-empty) parts of each matching line, one match per output line. Requires output_mode: "content", ignored otherwise. Defaults to false.""")][DefaultValue(false)] bool only_matching, [Description("""Number of lines to show before each match. Requires output_mode: "content", ignored otherwise.""")][DefaultValue(null)] int? before_context, [Description("""Number of lines to show after each match. Requires output_mode: "content", ignored otherwise.""")][DefaultValue(null)] int? after_context, [Description("""Number of lines to show before and after each match. Requires output_mode: "content", ignored otherwise.""")][DefaultValue(null)] int? context, [Description("""Glob pattern to filter files (e.g. "*.js", "*.{ts,tsx}")""")][DefaultValue(null)] string? glob, [Description("File type to search. Common types: js, py, rust, go, java, etc. More efficient than include for standard file types.")][DefaultValue(null)] string? type, [Description("""Output mode: "content" shows matching lines (supports context and line numbers), "files_with_matches" shows file paths, "count" shows match counts. Defaults to "files_with_matches".""")][DefaultValue("files_with_matches")] string? output_mode, [Description("""Limit output to first N lines/entries. Works across all output modes. Defaults to 250 when unspecified. Pass 0 for unlimited (use sparingly — large result sets waste context).""")][DefaultValue(250)] int? head_limit, [Description("""Skip first N lines/entries before applying head_limit. Works across all output modes. Defaults to 0.""")][DefaultValue(0)] int offset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("Search pattern cannot be empty.", nameof(pattern));
        }

        string normalizedOutputMode = NormalizeGrepOutputMode(output_mode);
        int normalizedOffset = NormalizeNonNegativeOption(offset, nameof(offset), 0);
        int normalizedHeadLimit = NormalizeNonNegativeOption(head_limit, nameof(head_limit), DefaultGrepHeadLimit);
        AgentWorkspacePath searchPath = Context.ResolveGrepSearchPath(path);
        ProcessStartInfo startInfo = CreateGrepStartInfo(searchPath, pattern, glob, type, normalizedOutputMode, context, before_context, after_context, ignore_case, show_line_numbers, only_matching, multiline);
        GrepProcessResult result = await RunRipgrepAsync(startInfo, cancellationToken);
        if (result.ExitCode is 1)
        {
            return [];
        }

        if (result.ExitCode is not 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? $"ripgrep failed with exit code {result.ExitCode}." : result.StandardError.Trim());
        }

        return WindowGrepOutput(CreateGrepOutputLines(result.StandardOutput, searchPath.Root), normalizedOffset, normalizedHeadLimit);
    }

    private static ProcessStartInfo CreateGrepStartInfo(AgentWorkspacePath searchPath, string pattern, string? glob, string? type, string outputMode, int? context, int? beforeContext, int? afterContext, bool ignoreCase, bool showLineNumbers, bool onlyMatching, bool multiline)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "rg",
            WorkingDirectory = searchPath.Root.Directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--color");
        startInfo.ArgumentList.Add("never");
        startInfo.ArgumentList.Add("--path-separator");
        startInfo.ArgumentList.Add("/");
        startInfo.ArgumentList.Add("--no-heading");
        startInfo.ArgumentList.Add("--with-filename");
        if (ignoreCase)
        {
            startInfo.ArgumentList.Add("--ignore-case");
        }

        if (multiline)
        {
            startInfo.ArgumentList.Add("--multiline");
            startInfo.ArgumentList.Add("--multiline-dotall");
        }

        if (!string.IsNullOrWhiteSpace(glob))
        {
            startInfo.ArgumentList.Add("--glob");
            startInfo.ArgumentList.Add(glob);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            startInfo.ArgumentList.Add("--type");
            startInfo.ArgumentList.Add(type);
        }

        if (string.Equals(outputMode, GrepFilesWithMatchesOutputMode, StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add("--files-with-matches");
        }
        else if (string.Equals(outputMode, GrepCountOutputMode, StringComparison.Ordinal))
        {
            startInfo.ArgumentList.Add("--count-matches");
        }
        else
        {
            if (showLineNumbers)
            {
                startInfo.ArgumentList.Add("--line-number");
            }
            else
            {
                startInfo.ArgumentList.Add("--no-line-number");
            }

            if (onlyMatching)
            {
                startInfo.ArgumentList.Add("--only-matching");
            }

            AddOptionalGrepNumberArgument(startInfo, "--context", context, nameof(context));
            AddOptionalGrepNumberArgument(startInfo, "--before-context", beforeContext, nameof(beforeContext));
            AddOptionalGrepNumberArgument(startInfo, "--after-context", afterContext, nameof(afterContext));
        }

        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(pattern);
        string relativePath = searchPath.Root.GetRelativePath(searchPath.FullPath);
        startInfo.ArgumentList.Add(string.Equals(relativePath, ".", StringComparison.Ordinal) ? "." : relativePath);
        return startInfo;
    }

    private static void AddOptionalGrepNumberArgument(ProcessStartInfo startInfo, string argumentName, int? value, string parameterName)
    {
        if (!value.HasValue)
        {
            return;
        }

        int normalizedValue = NormalizeNonNegativeOption(value, parameterName, 0);
        startInfo.ArgumentList.Add(argumentName);
        startInfo.ArgumentList.Add(normalizedValue.ToString(CultureInfo.InvariantCulture));
    }

    private static string NormalizeGrepOutputMode(string? outputMode)
    {
        if (string.IsNullOrWhiteSpace(outputMode))
        {
            return GrepFilesWithMatchesOutputMode;
        }

        string trimmedOutputMode = outputMode.Trim();
        if (string.Equals(trimmedOutputMode, GrepContentOutputMode, StringComparison.OrdinalIgnoreCase))
        {
            return GrepContentOutputMode;
        }

        if (string.Equals(trimmedOutputMode, GrepFilesWithMatchesOutputMode, StringComparison.OrdinalIgnoreCase))
        {
            return GrepFilesWithMatchesOutputMode;
        }

        if (string.Equals(trimmedOutputMode, GrepCountOutputMode, StringComparison.OrdinalIgnoreCase))
        {
            return GrepCountOutputMode;
        }

        throw new ArgumentException($"Unsupported output_mode: {outputMode}.", nameof(outputMode));
    }

    private static int NormalizeNonNegativeOption(int? value, string parameterName, int defaultValue)
    {
        if (!value.HasValue)
        {
            return defaultValue;
        }

        if (value.Value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Value cannot be negative.");
        }

        return value.Value;
    }

    private async Task<GrepProcessResult> RunRipgrepAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = startInfo,
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start ripgrep.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("ripgrep executable 'rg' was not found.", ex);
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            TryKillProcess(process);
            throw;
        }

        return new()
        {
            ExitCode = process.ExitCode,
            StandardOutput = await standardOutputTask,
            StandardError = await standardErrorTask,
        };
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }

    private List<string> CreateGrepOutputLines(string standardOutput, AgentWorkspaceRoot root)
    {
        List<string> lines = [];
        foreach (string rawLine in standardOutput.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').Replace('\\', '/');
            if (line.Length is 0)
            {
                continue;
            }

            lines.Add(CreateGrepDisplayLine(line, root));
        }

        return lines;
    }

    private string CreateGrepDisplayLine(string line, AgentWorkspaceRoot root)
    {
        if (string.Equals(line, "--", StringComparison.Ordinal))
        {
            return line;
        }

        string relativeLine = line.StartsWith("./", StringComparison.Ordinal) ? line[2..] : line;
        return $"{root.Directory.Replace('\\', '/')}/{relativeLine}";
    }

    private static List<string> WindowGrepOutput(List<string> lines, int offset, int headLimit)
    {
        List<string> results = [];
        if (offset >= lines.Count)
        {
            return results;
        }

        int exclusiveEnd = headLimit is 0 ? lines.Count : Math.Min(lines.Count, offset + headLimit);
        for (int i = offset; i < exclusiveEnd; i++)
        {
            results.Add(lines[i]);
        }

        return results;
    }

    private sealed class GrepProcessResult
    {
        public required int ExitCode { get; init; }

        public required string StandardOutput { get; init; }

        public required string StandardError { get; init; }
    }
}
