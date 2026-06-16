using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentWorkspaceFileAccessProvider : AIContextProvider
{
    private const string SaveFileFunctionName = "FileAccess_SaveFile";
    private const string ReadFileFunctionName = "FileAccess_ReadFile";
    private const string DeleteFileFunctionName = "FileAccess_DeleteFile";
    private const string ListDirectoryFunctionName = "FileAccess_ListDirectory";
    private const string SearchFilesFunctionName = "FileAccess_SearchFiles";
    private const int RegexTimeoutSeconds = 5;

    private readonly IReadOnlyList<WorkspaceRoot> roots;
    private readonly IReadOnlyList<AITool> tools;

    public AgentWorkspaceFileAccessProvider(IReadOnlyList<string> workingDirectories)
    {
        roots = CreateRoots(workingDirectories);
        tools =
        [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(SaveFileAsync, CreateFunctionOptions(SaveFileFunctionName, "Writes UTF-8 text to a file under a workspace root. This operation requires user approval."))),
            AIFunctionFactory.Create(ReadFileAsync, CreateFunctionOptions(ReadFileFunctionName, "Reads UTF-8 text from a file under a workspace root.")),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(DeleteFileAsync, CreateFunctionOptions(DeleteFileFunctionName, "Deletes a file under a workspace root. This operation requires user approval."))),
            AIFunctionFactory.Create(ListDirectoryAsync, CreateFunctionOptions(ListDirectoryFunctionName, "Lists direct child files and directories under a workspace directory.")),
            AIFunctionFactory.Create(SearchFilesAsync, CreateFunctionOptions(SearchFilesFunctionName, "Searches file paths under a workspace directory with a regular expression.")),
        ];
    }

    public override IReadOnlyList<string> StateKeys { get => Array.Empty<string>(); }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        AIContext aiContext = new()
        {
            Instructions = CreateInstructions(),
            Tools = tools,
        };

        return new(aiContext);
    }

    private async Task<string> SaveFileAsync([Description("Workspace-relative file path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/file.txt'. Absolute paths and '..' segments are rejected.")] string fileName, [Description("UTF-8 text content to write.")] string content, [Description("Set true to overwrite an existing file.")] bool overwrite = false, CancellationToken cancellationToken = default)
    {
        WorkspacePath path = ResolveFilePath(fileName);
        if (!overwrite && File.Exists(path.FullPath))
        {
            return $"File already exists: {path.DisplayPath}";
        }

        string? directory = Path.GetDirectoryName(path.FullPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path.FullPath, content, Encoding.UTF8, cancellationToken);
        return $"Saved file: {path.DisplayPath}";
    }

    private async Task<string> ReadFileAsync([Description("Workspace-relative file path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/file.txt'. Absolute paths and '..' segments are rejected.")] string fileName, CancellationToken cancellationToken = default)
    {
        WorkspacePath path = ResolveFilePath(fileName);
        if (!File.Exists(path.FullPath))
        {
            return $"File not found: {path.DisplayPath}";
        }

        return await File.ReadAllTextAsync(path.FullPath, Encoding.UTF8, cancellationToken);
    }

    private Task<string> DeleteFileAsync([Description("Workspace-relative file path. With multiple workspace roots, prefix the path with a workspace alias such as 'workspace1/file.txt'. Absolute paths and '..' segments are rejected.")] string fileName, CancellationToken cancellationToken = default)
    {
        WorkspacePath path = ResolveFilePath(fileName);
        if (!File.Exists(path.FullPath))
        {
            return Task.FromResult($"File not found: {path.DisplayPath}");
        }

        File.Delete(path.FullPath);
        return Task.FromResult($"Deleted file: {path.DisplayPath}");
    }

    private Task<IReadOnlyList<string>> ListDirectoryAsync([Description("Workspace-relative directory path. With multiple workspace roots, use empty or null to list workspace aliases, or prefix the path with a workspace alias such as 'workspace1/src'.")] string? directory = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directory) && roots.Count > 1)
        {
            return Task.FromResult(CreateRootAliasEntries());
        }

        WorkspacePath path = ResolveDirectoryPath(directory);
        if (!Directory.Exists(path.FullPath))
        {
            return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
        }

        List<string> entries = [];
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
                entries.Add($"{name}/");
            }
        }

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
                entries.Add(name);
            }
        }

        entries.Sort(GetPathComparer());
        return Task.FromResult((IReadOnlyList<string>)entries);
    }

    private Task<IReadOnlyList<string>> SearchFilesAsync([Description("Regular expression pattern matched against workspace-relative file paths. File contents are not read.")] string regexPattern, [Description("Workspace-relative directory path. With multiple workspace roots, use empty or null to search all roots, or prefix the path with a workspace alias such as 'workspace1/src'.")] string? directory = null, [Description("Optional glob pattern such as '*.cs', '*.md', or 'src/*.cs'.")] string? filePattern = null, CancellationToken cancellationToken = default)
    {
        FilePathSearch search = new()
        {
            Regex = new(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(RegexTimeoutSeconds)),
            FilePattern = filePattern,
        };
        IReadOnlyList<WorkspacePath> searchDirectories = ResolveSearchDirectories(directory);
        List<string> results = [];
        foreach (WorkspacePath searchDirectory in searchDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(searchDirectory.FullPath))
            {
                continue;
            }

            SearchDirectory(searchDirectory, search, results, cancellationToken);
        }

        results.Sort(GetPathComparer());
        return Task.FromResult((IReadOnlyList<string>)results);
    }

    private void SearchDirectory(WorkspacePath searchDirectory, FilePathSearch search, List<string> results, CancellationToken cancellationToken)
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
            if (!MatchesFilePattern(relativePath, search.FilePattern) || !search.Regex.IsMatch(displayPath))
            {
                continue;
            }

            results.Add(displayPath);
        }
    }

    private string CreateInstructions()
    {
        StringBuilder builder = new();
        if (roots.Count is 1)
        {
            builder.AppendLine("Workspace file access is rooted at:");
            builder.AppendLine(roots[0].Directory);
            builder.AppendLine("Use only relative paths for workspace file operations.");
        }
        else
        {
            builder.AppendLine("Workspace file access has multiple roots:");
            foreach (WorkspaceRoot root in roots)
            {
                builder.AppendLine($"{root.Alias}: {root.Directory}");
            }

            builder.AppendLine("Use paths prefixed with a workspace alias, for example workspace1/README.md. Use FileAccess_ListDirectory with an empty path to list workspace aliases.");
        }

        builder.Append("Absolute paths and parent-directory traversal are rejected. Directory entries ending with '/' are directories. Saving and deleting files require explicit user approval.");
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

    private IReadOnlyList<WorkspacePath> ResolveSearchDirectories(string? path)
    {
        string normalizedPath = NormalizeDirectoryPath(path);
        if (normalizedPath.Length is 0 && roots.Count > 1)
        {
            List<WorkspacePath> paths = [];
            foreach (WorkspaceRoot root in roots)
            {
                paths.Add(CreateWorkspacePath(root, string.Empty));
            }

            return paths;
        }

        return [ResolveWorkspacePath(normalizedPath)];
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

    private IReadOnlyList<string> CreateRootAliasEntries()
    {
        List<string> entries = [];
        foreach (WorkspaceRoot root in roots)
        {
            entries.Add($"{root.Alias}/");
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

    private static bool MatchesFilePattern(string relativePath, string? filePattern)
    {
        if (string.IsNullOrWhiteSpace(filePattern))
        {
            return true;
        }

        string normalizedPattern = filePattern.Trim().Replace('\\', '/');
        string fileName = Path.GetFileName(relativePath);
        return FileSystemName.MatchesSimpleExpression(normalizedPattern, relativePath, true) || FileSystemName.MatchesSimpleExpression(normalizedPattern, fileName, true);
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

    private sealed class FilePathSearch
    {
        public required Regex Regex { get; init; }

        public string? FilePattern { get; init; }
    }
}
