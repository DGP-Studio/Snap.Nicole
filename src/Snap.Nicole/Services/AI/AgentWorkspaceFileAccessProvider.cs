using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentWorkspaceFileAccessProvider : AIContextProvider
{
    private const string SaveFileFunctionName = "file_access_save_file";
    private const string ReadFileFunctionName = "file_access_read_file";
    private const string DeleteFileFunctionName = "file_access_delete_file";
    private const string ListFilesFunctionName = "file_access_list_files";
    private const string ListSubdirectoriesFunctionName = "file_access_list_subdirectories";
    private const string SearchFilesFunctionName = "file_access_search_files";
    private const int RegexTimeoutSeconds = 5;
    private const int SearchSnippetContextLength = 50;

    private readonly IReadOnlyList<WorkspaceRoot> roots;
    private readonly IReadOnlyList<AITool> tools;

    public AgentWorkspaceFileAccessProvider(IReadOnlyList<string> workingDirectories)
    {
        roots = CreateRoots(workingDirectories);
        tools =
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(SaveFileAsync, CreateFunctionOptions(SaveFileFunctionName, "Save a UTF-8 text file under a workspace root. By default, does not overwrite an existing file unless overwrite is set to true. This operation requires user approval."))),
            AIFunctionFactory.Create(ReadFileAsync, CreateFunctionOptions(ReadFileFunctionName, "Read the UTF-8 text content of a file under a workspace root. Returns the file content or a message indicating the file was not found.")),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DeleteFileAsync, CreateFunctionOptions(DeleteFileFunctionName, "Delete a file under a workspace root. This operation requires user approval."))),
            AIFunctionFactory.Create(ListFilesAsync, CreateFunctionOptions(ListFilesFunctionName, "List the direct child file names of a workspace directory. Omit the directory, or pass an empty string, to list the root.")),
            AIFunctionFactory.Create(ListSubdirectoriesAsync, CreateFunctionOptions(ListSubdirectoriesFunctionName, "List the direct child subdirectory names of a workspace directory. Omit the directory, or pass an empty string, to list the root.")),
            AIFunctionFactory.Create(SearchFilesAsync, CreateFunctionOptions(SearchFilesFunctionName, "Search the contents of all workspace files recursively using a regular expression pattern. Optionally filter which files to search using a glob pattern.")),
        ];
    }

    public override IReadOnlyList<string> StateKeys { get => []; }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext aiContext = new()
        {
            Instructions = CreateInstructions(),
            Tools = tools,
        };

        return new(aiContext);
    }

    [Description("Save a file with the given name and content. By default, does not overwrite an existing file unless overwrite is set to true. This operation requires user approval.")]
    private async Task<string> SaveFileAsync([Description("Workspace-relative file path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/file.txt'. Absolute paths and '..' segments are rejected.")] string fileName, [Description("UTF-8 text content to write.")] string content, [Description("Whether to overwrite the file if it already exists.")] bool overwrite = false, CancellationToken cancellationToken = default)
    {
        WorkspacePath path = ResolveFilePath(fileName);
        if (!overwrite && File.Exists(path.FullPath))
        {
            return $"File '{path.DisplayPath}' already exists. To replace it, save again with overwrite set to true.";
        }

        string? directory = Path.GetDirectoryName(path.FullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path.FullPath, content, Encoding.UTF8, cancellationToken);
        return $"File '{path.DisplayPath}' saved.";
    }

    [Description("Read the content of a file by name. Returns the file content or a message indicating the file was not found.")]
    private async Task<string> ReadFileAsync([Description("Workspace-relative file path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/file.txt'. Absolute paths and '..' segments are rejected.")] string fileName, CancellationToken cancellationToken = default)
    {
        WorkspacePath path = ResolveFilePath(fileName);
        if (!File.Exists(path.FullPath))
        {
            return $"File '{path.DisplayPath}' not found.";
        }

        return await File.ReadAllTextAsync(path.FullPath, Encoding.UTF8, cancellationToken);
    }

    [Description("Delete a file by name. This operation requires user approval.")]
    private Task<string> DeleteFileAsync([Description("Workspace-relative file path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/file.txt'. Absolute paths and '..' segments are rejected.")] string fileName, CancellationToken cancellationToken = default)
    {
        WorkspacePath path = ResolveFilePath(fileName);
        if (!File.Exists(path.FullPath))
        {
            return Task.FromResult($"File '{path.DisplayPath}' not found.");
        }

        File.Delete(path.FullPath);
        return Task.FromResult($"File '{path.DisplayPath}' deleted.");
    }

    [Description("List the direct child file names of a directory. Omit the directory, or pass an empty string, to list the root. To enumerate files in a subdirectory, pass its workspace-relative path.")]
    private Task<List<string>> ListFilesAsync([Description("Workspace-relative directory path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/src'. Omit or pass an empty string to list the root.")] string? directory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory) && roots.Count > 1)
        {
            return Task.FromResult(new List<string>());
        }

        WorkspacePath path = ResolveDirectoryPath(directory);
        if (!Directory.Exists(path.FullPath))
        {
            return Task.FromResult(new List<string>());
        }

        List<string> fileNames = [];
        foreach (string childFile in Directory.EnumerateFiles(path.FullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasReparsePoint(childFile))
            {
                continue;
            }

            string? name = Path.GetFileName(childFile);
            if (name is not null)
            {
                fileNames.Add(name);
            }
        }

        fileNames.Sort(GetPathComparer());
        return Task.FromResult(fileNames);
    }

    [Description("List the direct child subdirectory names of a directory. Omit the directory, or pass an empty string, to list the root. To enumerate subdirectories of a subdirectory, pass its workspace-relative path. Use this together with file_access_list_files to explore the directory tree level by level.")]
    private Task<List<string>> ListSubdirectoriesAsync([Description("Workspace-relative directory path. With multiple workspace roots, use empty or null to list workspace aliases, or prefix the path with a workspace alias such as 'workspace1/src'.")] string? directory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory) && roots.Count > 1)
        {
            return Task.FromResult(CreateRootAliasEntries());
        }

        WorkspacePath path = ResolveDirectoryPath(directory);
        if (!Directory.Exists(path.FullPath))
        {
            return Task.FromResult(new List<string>());
        }

        List<string> directoryNames = [];
        foreach (string childDirectory in Directory.EnumerateDirectories(path.FullPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasReparsePoint(childDirectory))
            {
                continue;
            }

            string? name = Path.GetFileName(childDirectory);
            if (name is not null)
            {
                directoryNames.Add(name);
            }
        }

        directoryNames.Sort(GetPathComparer());
        return Task.FromResult(directoryNames);
    }

    [Description(
        """
        Search the contents of all workspace files recursively, across all subdirectories, using a regular expression pattern (case-insensitive).
        Optionally filter which files to search using a glob pattern matched against each file's path as returned by file_access_read_file:
        - '*' matches within a single path segment
        - '**' matches across subdirectories, so use "**/*.md" to match markdown files at any depth, or "workspace1/src/**" to restrict the search to a subtree.

        Returns matching results whose file names are workspace-relative paths usable with file_access_read_file, along with snippets and matching lines with line numbers.
        """)]
    private async Task<List<FileSearchResult>> SearchFilesAsync([Description("A regular expression pattern to match against file contents, case-insensitively.")] string regexPattern, [Description("Optional glob pattern to filter which files to search, such as '*.cs', '**/*.md', 'src/**/*.cs', or 'workspace1/src/**'. Leave empty or omit to search all workspace files.")] string? filePattern = null, CancellationToken cancellationToken = default)
    {
        FileContentSearch search = new()
        {
            Regex = new(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(RegexTimeoutSeconds)),
            FilePattern = CreateFilePatternMatcher(filePattern),
        };
        List<FileSearchResult> results = [];
        foreach (WorkspaceRoot root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspacePath searchDirectory = CreateWorkspacePath(root, string.Empty);
            if (!Directory.Exists(searchDirectory.FullPath))
            {
                continue;
            }

            await SearchDirectoryAsync(searchDirectory, search, results, cancellationToken);
        }

        results.Sort((left, right) => GetPathComparer().Compare(left.FileName, right.FileName));
        return results;
    }

    private async Task SearchDirectoryAsync(WorkspacePath searchDirectory, FileContentSearch search, List<FileSearchResult> results, CancellationToken cancellationToken)
    {
        EnumerationOptions options = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
        };

        foreach (string filePath in Directory.EnumerateFiles(searchDirectory.FullPath, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = GetRootRelativePath(searchDirectory.Root, filePath);
            string displayPath = CreateDisplayPath(searchDirectory.Root, relativePath);
            if (search.FilePattern is not null && !search.FilePattern.Matches(relativePath, displayPath))
            {
                continue;
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
            }
            catch (Exception ex) when (AgentWorkspaceProvider.IsWorkspaceException(ex))
            {
                continue;
            }

            FileSearchResult? result = CreateSearchResult(displayPath, content, search.Regex);
            if (result is not null)
            {
                results.Add(result);
            }
        }
    }

    private static FileSearchResult? CreateSearchResult(string fileName, string content, Regex regex)
    {
        string[] lines = content.Split('\n');
        List<FileSearchMatch> matchingLines = [];
        string? firstSnippet = null;
        int lineStartOffset = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            Match match = regex.Match(lines[i]);
            if (match.Success)
            {
                matchingLines.Add(new()
                {
                    LineNumber = i + 1,
                    Line = lines[i].TrimEnd('\r'),
                });

                if (firstSnippet is null)
                {
                    int charIndex = lineStartOffset + match.Index;
                    int snippetStart = Math.Max(0, charIndex - SearchSnippetContextLength);
                    int snippetEnd = Math.Min(content.Length, charIndex + match.Value.Length + SearchSnippetContextLength);
                    firstSnippet = content.Substring(snippetStart, snippetEnd - snippetStart);
                }
            }

            lineStartOffset += lines[i].Length + 1;
        }

        if (matchingLines.Count is 0)
        {
            return null;
        }

        return new()
        {
            FileName = fileName,
            Snippet = firstSnippet ?? string.Empty,
            MatchingLines = matchingLines,
        };
    }

    private string CreateInstructions()
    {
        StringBuilder builder = new();
        builder.AppendLine("## Workspace File Access");
        builder.AppendLine("You have access to workspace files via the `file_access_*` tools for reading, writing, deleting, listing, and searching files.");
        builder.AppendLine("These files persist beyond the current session and may be shared by future sessions that use the same workspace.");
        builder.AppendLine("Use these tools to read input data provided by the user, write output artifacts, and manage files the user has asked you to work with.");
        builder.AppendLine();
        if (roots.Count is 1)
        {
            builder.AppendLine("The workspace root is:");
            builder.AppendLine(roots[0].Directory);
            builder.AppendLine("Use paths relative to this root, for example `src/file.cs`.");
        }
        else
        {
            builder.AppendLine("The accessible workspace roots are:");
            foreach (WorkspaceRoot root in roots)
            {
                builder.AppendLine($"- `{root.Alias}`: {root.Directory}");
            }

            builder.AppendLine("Use paths prefixed with a workspace alias, for example `workspace1/README.md`. At the virtual root, `file_access_list_subdirectories` returns the available aliases.");
        }

        builder.AppendLine();
        builder.AppendLine("- Never delete or overwrite existing files unless the user has explicitly asked you to do so. Saving and deleting files require explicit user approval.");
        builder.AppendLine("- Absolute paths and parent-directory traversal are rejected.");
        builder.Append("- Files may be organized into subdirectories. Use `file_access_list_files` and `file_access_list_subdirectories` to explore the tree level by level, or `file_access_search_files` to search file contents recursively across the whole workspace.");
        return builder.ToString();
    }

    private static AIFunctionFactoryOptions CreateFunctionOptions(string name, string description)
    {
        return new()
        {
            Name = name,
            Description = description,
        };
    }

    private WorkspacePath ResolveFilePath(string path)
    {
        string normalizedPath = NormalizeFilePath(path);
        WorkspacePath workspacePath = ResolveWorkspacePath(normalizedPath);
        if (workspacePath.RelativePath.Length is 0)
        {
            throw new ArgumentException("File path cannot target a workspace root.", nameof(path));
        }

        return workspacePath;
    }

    private WorkspacePath ResolveDirectoryPath(string? path)
    {
        string normalizedPath = NormalizeDirectoryPath(path);
        return ResolveWorkspacePath(normalizedPath);
    }

    private WorkspacePath ResolveWorkspacePath(string normalizedPath)
    {
        if (roots.Count is 1)
        {
            WorkspaceRoot root = roots[0];
            if (TryGetAliasPrefixedPath(normalizedPath, out string alias, out string relativePath) && IsRootAlias(root, alias))
            {
                return CreateWorkspacePath(root, relativePath);
            }

            return CreateWorkspacePath(root, normalizedPath);
        }

        if (!TryGetAliasPrefixedPath(normalizedPath, out string requestedAlias, out string requestedRelativePath))
        {
            throw new ArgumentException($"Path must start with one of the workspace aliases: {CreateRootAliasList()}", nameof(normalizedPath));
        }

        foreach (WorkspaceRoot root in roots)
        {
            if (IsRootAlias(root, requestedAlias))
            {
                return CreateWorkspacePath(root, requestedRelativePath);
            }
        }

        throw new ArgumentException($"Unknown workspace alias: {requestedAlias}", nameof(normalizedPath));
    }

    private WorkspacePath CreateWorkspacePath(WorkspaceRoot root, string relativePath)
    {
        string fullPath = ResolveSafeFullPath(root, relativePath);
        return new()
        {
            Root = root,
            RelativePath = relativePath,
            FullPath = fullPath,
            DisplayPath = CreateDisplayPath(root, relativePath),
        };
    }

    private string ResolveSafeFullPath(WorkspaceRoot root, string relativePath)
    {
        string localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root.Directory, localPath));
        StringComparison comparison = GetPathComparison();
        if (!string.Equals(fullPath, root.Directory, comparison) && !fullPath.StartsWith(root.RootPath, comparison))
        {
            throw new ArgumentException($"Invalid path: '{relativePath}'. The resolved path escapes the workspace root.", nameof(relativePath));
        }

        ThrowIfContainsReparsePoint(fullPath, root.Directory);
        return fullPath;
    }

    private string CreateDisplayPath(WorkspaceRoot root, string relativePath)
    {
        if (roots.Count is 1)
        {
            return relativePath;
        }

        if (relativePath.Length is 0)
        {
            return root.Alias;
        }

        return $"{root.Alias}/{relativePath}";
    }

    private List<string> CreateRootAliasEntries()
    {
        List<string> entries = [];
        foreach (WorkspaceRoot root in roots)
        {
            entries.Add(root.Alias);
        }

        return entries;
    }

    private string CreateRootAliasList()
    {
        List<string> aliases = [];
        foreach (WorkspaceRoot root in roots)
        {
            aliases.Add(root.Alias);
        }

        return string.Join(", ", aliases);
    }

    private static string GetRootRelativePath(WorkspaceRoot root, string fullPath)
    {
        return Path.GetRelativePath(root.Directory, fullPath).Replace('\\', '/');
    }

    private static FilePatternMatcher? CreateFilePatternMatcher(string? filePattern)
    {
        if (string.IsNullOrWhiteSpace(filePattern))
        {
            return null;
        }

        string normalizedPattern = filePattern.Trim().Replace('\\', '/');
        Regex pathRegex = new(CreateGlobRegexPattern(normalizedPattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(RegexTimeoutSeconds));
        Regex? fileNameRegex = normalizedPattern.Contains('/', StringComparison.Ordinal) ? null : pathRegex;
        return new()
        {
            PathRegex = pathRegex,
            FileNameRegex = fileNameRegex,
        };
    }

    private static string CreateGlobRegexPattern(string pattern)
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

    private static string NormalizeFilePath(string path)
    {
        string normalizedPath = NormalizeRelativePath(path);
        if (normalizedPath.Length is 0)
        {
            throw new ArgumentException("File path cannot be empty.", nameof(path));
        }

        return normalizedPath;
    }

    private static string NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return NormalizeRelativePath(path);
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalizedPath = path.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath) || normalizedPath.StartsWith("/", StringComparison.Ordinal) || IsWindowsDriveRootedPath(normalizedPath))
        {
            throw new ArgumentException($"Path must be relative to the workspace root: {path}", nameof(path));
        }

        string[] segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (segment is "." or "..")
            {
                throw new ArgumentException($"Path cannot contain traversal segments: {path}", nameof(path));
            }
        }

        return string.Join("/", segments);
    }

    private static bool TryGetAliasPrefixedPath(string normalizedPath, out string alias, out string relativePath)
    {
        if (normalizedPath.Length is 0)
        {
            alias = string.Empty;
            relativePath = string.Empty;
            return false;
        }

        int separatorIndex = normalizedPath.IndexOf('/');
        if (separatorIndex < 0)
        {
            alias = normalizedPath;
            relativePath = string.Empty;
            return true;
        }

        alias = normalizedPath[..separatorIndex];
        relativePath = normalizedPath[(separatorIndex + 1)..];
        return true;
    }

    private static bool IsRootAlias(WorkspaceRoot root, string alias)
    {
        return string.Equals(root.Alias, alias, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsDriveRootedPath(string path)
    {
        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] is ':';
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != FileAttributes.None;
        }
        catch (Exception ex) when (AgentWorkspaceProvider.IsWorkspaceException(ex))
        {
            return true;
        }
    }

    private static void ThrowIfContainsReparsePoint(string fullPath, string rootPath)
    {
        string root = Path.TrimEndingDirectorySeparator(rootPath);
        if (fullPath.Length <= root.Length)
        {
            return;
        }

        string[] segments = fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        string currentPath = root;
        for (int i = 0; i < segments.Length; i++)
        {
            currentPath = Path.Combine(currentPath, segments[i]);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(currentPath);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != FileAttributes.None)
            {
                throw new ArgumentException("Invalid path: the resolved path contains a symbolic link or reparse point.");
            }
        }
    }

    private static IReadOnlyList<WorkspaceRoot> CreateRoots(IReadOnlyList<string> workingDirectories)
    {
        ArgumentNullException.ThrowIfNull(workingDirectories);
        if (workingDirectories.Count is 0)
        {
            throw new ArgumentException("At least one workspace directory is required.", nameof(workingDirectories));
        }

        List<WorkspaceRoot> createdRoots = [];
        for (int i = 0; i < workingDirectories.Count; i++)
        {
            string directory = Path.GetFullPath(workingDirectories[i]);
            Directory.CreateDirectory(directory);
            createdRoots.Add(new()
            {
                Alias = workingDirectories.Count is 1 ? string.Empty : $"workspace{i + 1}",
                Directory = Path.TrimEndingDirectorySeparator(directory),
                RootPath = EnsureTrailingDirectorySeparator(directory),
            });
        }

        return createdRoots;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) || fullPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return fullPath;
        }

        return fullPath + Path.DirectorySeparatorChar;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private sealed class WorkspaceRoot
    {
        public required string Alias { get; init; }

        public required string Directory { get; init; }

        public required string RootPath { get; init; }
    }

    private sealed class WorkspacePath
    {
        public required WorkspaceRoot Root { get; init; }

        public required string RelativePath { get; init; }

        public required string FullPath { get; init; }

        public required string DisplayPath { get; init; }
    }

    private sealed class FileContentSearch
    {
        public required Regex Regex { get; init; }

        public FilePatternMatcher? FilePattern { get; init; }
    }

    private sealed class FilePatternMatcher
    {
        public required Regex PathRegex { get; init; }

        public Regex? FileNameRegex { get; init; }

        public bool Matches(string relativePath, string displayPath)
        {
            if (PathRegex.IsMatch(relativePath) || PathRegex.IsMatch(displayPath))
            {
                return true;
            }

            string fileName = Path.GetFileName(relativePath);
            return FileNameRegex is not null && FileNameRegex.IsMatch(fileName);
        }
    }
}
