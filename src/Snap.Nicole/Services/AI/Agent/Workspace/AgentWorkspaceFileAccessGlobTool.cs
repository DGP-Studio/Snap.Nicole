using Microsoft.Extensions.AI;
using Snap.Nicole.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessGlobTool : AgentWorkspaceFileAccessToolComponent
{
    private const int RegexTimeoutSeconds = 5;

    private readonly AITool tool;

    public AgentWorkspaceFileAccessGlobTool(AgentWorkspaceFileAccessContext context) : base(context)
    {
        tool = AIFunctionFactory.Create(GlobAsync);
    }

    public override AITool Tool { get => tool; }

    [DisplayName(AgentWorkspaceFileAccessToolNames.Glob)]
    [Description("""
        Fast file pattern matching. Supports glob patterns like "**/*.js" or "src/**/*.ts". Returns matching file paths sorted by modification time.
        """)]
    private Task<List<string>> GlobAsync(
        [Description("""The absolute directory to search in. If not specified, the current working directory will be used. IMPORTANT: Omit this field to use the default directory. DO NOT enter "undefined" or "null" - simply omit it for the default behavior.""")][DefaultValue(null)] string? path,
        [Description("The glob pattern to match files against")] string pattern,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern, "Glob pattern cannot be empty.", nameof(pattern));
        FilePatternMatcher patternMatcher = CreateFilePatternMatcher(pattern);
        AgentWorkspacePath searchDirectory = Context.ResolveGlobDirectoryPath(path);
        if (!Directory.Exists(searchDirectory.FullPath))
        {
            return Task.FromResult(new List<string>());
        }

        EnumerationOptions options = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        List<FileGlobResult> results = [];
        foreach (string filePath in Directory.EnumerateFiles(searchDirectory.FullPath, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string searchRelativePath = Path.GetRelativePath(searchDirectory.FullPath, filePath).Replace('\\', '/');
            string rootRelativePath = searchDirectory.Root.GetRelativePath(filePath);
            string fullPath = Path.GetFullPath(filePath);
            string normalizedFullPath = fullPath.Replace('\\', '/');
            if (!patternMatcher.Matches(searchRelativePath, normalizedFullPath) && !patternMatcher.Matches(rootRelativePath, normalizedFullPath))
            {
                continue;
            }

            results.Add(new()
            {
                FilePath = fullPath,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(filePath),
            });
        }

        results.Sort(static (left, right) =>
        {
            int result = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
            return result is not 0 ? result : string.Compare(left.FilePath, right.FilePath, StringComparison.Ordinal);
        });

        List<string> paths = [];
        foreach (FileGlobResult result in results)
        {
            paths.Add(result.FilePath);
        }

        return Task.FromResult(paths);
    }

    private static FilePatternMatcher CreateFilePatternMatcher(string filePattern)
    {
        int length = filePattern.Length;
        Span<char> normalizedPattern = length <= 1024 ? stackalloc char[length] : new char[length];
        filePattern.AsSpan().CopyTo(normalizedPattern);
        normalizedPattern.Replace('\\', '/');

        Regex pathRegex = new(CreateGlobRegexPattern(normalizedPattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(RegexTimeoutSeconds));
        Regex? fileNameRegex = normalizedPattern.Contains('/') ? null : pathRegex;

        return new()
        {
            PathRegex = pathRegex,
            FileNameRegex = fileNameRegex,
        };
    }

    private static string CreateGlobRegexPattern(ReadOnlySpan<char> pattern)
    {
        StringBuilder builder = new();
        builder.Append('^');
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch is '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] is '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] is '/')
                    {
                        builder.Append("(?:.*/)?");
                        i += 2;
                    }
                    else
                    {
                        builder.Append(".*");
                        i++;
                    }
                }
                else
                {
                    builder.Append("[^/]*");
                }
            }
            else if (ch is '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(ch.ToString()));
            }
        }

        builder.Append('$');
        return builder.ToString();
    }

    private sealed class FileGlobResult
    {
        public required string FilePath { get; init; }

        public required DateTime LastWriteTimeUtc { get; init; }
    }

    private sealed class FilePatternMatcher
    {
        public required Regex PathRegex { get; init; }

        public Regex? FileNameRegex { get; init; }

        public bool Matches(string relativePath, string fullPath)
        {
            if (PathRegex.IsMatch(relativePath) || PathRegex.IsMatch(fullPath))
            {
                return true;
            }

            string fileName = Path.GetFileName(relativePath);
            return FileNameRegex is not null && FileNameRegex.IsMatch(fileName);
        }
    }
}
