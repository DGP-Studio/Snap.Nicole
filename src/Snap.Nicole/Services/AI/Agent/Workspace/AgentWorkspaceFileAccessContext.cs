using Snap.Nicole.Core.IO;
using System;
using System.Collections.Generic;
using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessContext
{
    private readonly IReadOnlyList<WorkspaceRoot> roots;
    private readonly HashSet<string> readFilePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object readFilePathsLock = new();

    public AgentWorkspaceFileAccessContext(IReadOnlyList<string> workingDirectories)
    {
        roots = CreateRoots(workingDirectories);
    }

    public IReadOnlyList<WorkspaceRoot> Roots { get => roots; }

    public WorkspacePath ResolveAbsoluteFilePath(string path)
    {
        string trimmedPath = path.Trim();
        if (trimmedPath.Length is 0)
        {
            throw new ArgumentException("File path cannot be empty.", nameof(path));
        }

        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            throw new ArgumentException($"File path must be absolute: {path}", nameof(path));
        }

        string fullPath = Path.GetFullPath(trimmedPath);
        foreach (WorkspaceRoot root in roots)
        {
            if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
            {
                continue;
            }

            string relativePath = GetRootRelativePath(root, fullPath);
            if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            {
                throw new ArgumentException("File path cannot target a workspace root.", nameof(path));
            }

            ThrowIfContainsReparsePoint(fullPath, root.Directory);
            return new()
            {
                Root = root,
                RelativePath = relativePath,
                FullPath = fullPath,
                DisplayPath = CreateDisplayPath(root, relativePath),
            };
        }

        throw new ArgumentException($"File path must be under a workspace root: {path}", nameof(path));
    }

    public WorkspacePath ResolveGlobDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspacePath(roots[0], string.Empty);
        }

        string trimmedPath = path.Trim();
        if (Path.IsPathFullyQualified(trimmedPath))
        {
            return ResolveAbsoluteDirectoryPath(trimmedPath);
        }

        string normalizedPath = NormalizeDirectoryPath(trimmedPath);
        if (roots.Count > 1 && TryGetAliasPrefixedPath(normalizedPath, out string alias, out _) && IsKnownRootAlias(alias))
        {
            return ResolveWorkspacePath(normalizedPath);
        }

        return CreateWorkspacePath(roots[0], normalizedPath);
    }

    public WorkspacePath ResolveGrepSearchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspacePath(roots[0], string.Empty);
        }

        string trimmedPath = path.Trim();
        if (string.Equals(trimmedPath, "undefined", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmedPath, "null", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Omit path instead of passing undefined or null.", nameof(path));
        }

        if (Path.IsPathFullyQualified(trimmedPath))
        {
            return ResolveAbsoluteSearchPath(trimmedPath);
        }

        string normalizedPath = NormalizeDirectoryPath(trimmedPath);
        if (roots.Count > 1 && TryGetAliasPrefixedPath(normalizedPath, out string alias, out _) && IsKnownRootAlias(alias))
        {
            return ResolveWorkspacePath(normalizedPath);
        }

        return CreateWorkspacePath(roots[0], normalizedPath);
    }

    public string GetRootRelativePath(WorkspaceRoot root, string fullPath)
    {
        return Path.GetRelativePath(root.Directory, fullPath).Replace('\\', '/');
    }

    public string CreateDisplayPath(WorkspaceRoot root, string relativePath)
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

    public void MarkFileAsRead(string fullPath)
    {
        lock (readFilePathsLock)
        {
            readFilePaths.Add(fullPath);
        }
    }

    public bool WasFileRead(string fullPath)
    {
        lock (readFilePathsLock)
        {
            return readFilePaths.Contains(fullPath);
        }
    }

    private WorkspacePath ResolveAbsoluteDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (WorkspaceRoot root in roots)
        {
            if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
            {
                continue;
            }

            string relativePath = GetRootRelativePath(root, fullPath);
            if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            {
                relativePath = string.Empty;
            }

            ThrowIfContainsReparsePoint(fullPath, root.Directory);
            return new()
            {
                Root = root,
                RelativePath = relativePath,
                FullPath = fullPath,
                DisplayPath = CreateDisplayPath(root, relativePath),
            };
        }

        throw new ArgumentException($"Directory path must be under a workspace root: {path}", nameof(path));
    }

    private WorkspacePath ResolveAbsoluteSearchPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (WorkspaceRoot root in roots)
        {
            if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
            {
                continue;
            }

            string relativePath = GetRootRelativePath(root, fullPath);
            if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            {
                relativePath = string.Empty;
            }

            ThrowIfContainsReparsePoint(fullPath, root.Directory);
            return new()
            {
                Root = root,
                RelativePath = relativePath,
                FullPath = fullPath,
                DisplayPath = CreateDisplayPath(root, relativePath),
            };
        }

        throw new ArgumentException($"Search path must be under a workspace root: {path}", nameof(path));
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
        if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
        {
            throw new ArgumentException($"Invalid path: '{relativePath}'. The resolved path escapes the workspace root.", nameof(relativePath));
        }

        ThrowIfContainsReparsePoint(fullPath, root.Directory);
        return fullPath;
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

    private bool IsKnownRootAlias(string alias)
    {
        foreach (WorkspaceRoot root in roots)
        {
            if (IsRootAlias(root, alias))
            {
                return true;
            }
        }

        return false;
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
            });
        }

        return createdRoots;
    }

    public sealed class WorkspaceRoot
    {
        public required string Alias { get; init; }

        public required string Directory { get; init; }
    }

    public sealed class WorkspacePath
    {
        public required WorkspaceRoot Root { get; init; }

        public required string RelativePath { get; init; }

        public required string FullPath { get; init; }

        public required string DisplayPath { get; init; }
    }
}
