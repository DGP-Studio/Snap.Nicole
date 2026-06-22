using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceFileAccessContext(IReadOnlyList<string> workingDirectories)
{
    private readonly Lock readFilePathsLock = new();
    private readonly HashSet<string> readFilePaths = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AgentWorkspaceRoot> Roots { get; } = CreateRoots(workingDirectories);

    public AgentWorkspacePath ResolveAbsoluteFilePath(string path)
    {
        string trimmedPath = path.Trim();
        ArgumentException.ThrowIfNullOrEmpty(trimmedPath, "File path cannot be empty", nameof(path));
        ArgumentException.ThrowIfNot(Path.IsPathFullyQualified(trimmedPath), $"File path must be absolute: {path}", nameof(path));

        string fullPath = Path.GetFullPath(trimmedPath);
        foreach (AgentWorkspaceRoot root in Roots)
        {
            if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
            {
                continue;
            }

            // We use relativePath variable below, so Path.IsEqual is not used here
            string relativePath = root.GetRelativePath(fullPath);
            ArgumentException.ThrowIf(string.Equals(relativePath, ".", StringComparison.Ordinal), "File path cannot target a workspace root.", nameof(path));

            ThrowIfContainsReparsePoint(root.Directory, fullPath);

            return new()
            {
                Root = root,
                FullPath = fullPath,
            };
        }

        throw new ArgumentException($"File path must be under a workspace root: {path}", nameof(path));
    }

    public AgentWorkspacePath ResolveGlobDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspacePath(Roots[0], string.Empty);
        }

        string trimmedPath = path.Trim();
        if (Path.IsPathFullyQualified(trimmedPath))
        {
            return ResolveAbsoluteDirectoryPath(trimmedPath);
        }

        throw new ArgumentException($"Directory path must be absolute: {path}", nameof(path));
    }

    public AgentWorkspacePath ResolveGrepSearchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspacePath(Roots[0], string.Empty);
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

        throw new ArgumentException($"Search path must be absolute: {path}", nameof(path));
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

    private AgentWorkspacePath ResolveAbsoluteDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (AgentWorkspaceRoot root in Roots)
        {
            if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
            {
                continue;
            }

            ThrowIfContainsReparsePoint(root.Directory, fullPath);
            return new()
            {
                Root = root,
                FullPath = fullPath,
            };
        }

        throw new ArgumentException($"Directory path must be under a workspace root: {path}", nameof(path));
    }

    private AgentWorkspacePath ResolveAbsoluteSearchPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (AgentWorkspaceRoot root in Roots)
        {
            if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
            {
                continue;
            }

            ThrowIfContainsReparsePoint(root.Directory, fullPath);
            return new()
            {
                Root = root,
                FullPath = fullPath,
            };
        }

        throw new ArgumentException($"Search path must be under a workspace root: {path}", nameof(path));
    }

    private AgentWorkspacePath CreateWorkspacePath(AgentWorkspaceRoot root, string relativePath)
    {
        string fullPath = ResolveSafeFullPath(root, relativePath);
        return new()
        {
            Root = root,
            FullPath = fullPath,
        };
    }

    private string ResolveSafeFullPath(AgentWorkspaceRoot root, string relativePath)
    {
        string localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root.Directory, localPath));
        if (!Path.IsEqualOrSubdirectory(root.Directory, fullPath))
        {
            throw new ArgumentException($"Invalid path: '{relativePath}'. The resolved path escapes the workspace root.", nameof(relativePath));
        }

        ThrowIfContainsReparsePoint(root.Directory, fullPath);
        return fullPath;
    }

    private static void ThrowIfContainsReparsePoint(string rootPath, string fullPath)
    {
        string root = Path.TrimEndingDirectorySeparator(rootPath);
        string currentPath = Path.TrimEndingDirectorySeparator(fullPath);

        while (currentPath.Length > root.Length)
        {
            try
            {
                if (File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ArgumentException("Invalid path: the resolved path contains a symbolic link or reparse point.");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            string? parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parentPath))
            {
                break;
            }

            currentPath = Path.TrimEndingDirectorySeparator(parentPath);
        }
    }

    private static List<AgentWorkspaceRoot> CreateRoots(IReadOnlyList<string> workingDirectories)
    {
        ArgumentNullException.ThrowIfNull(workingDirectories);
        ArgumentException.ThrowIfEmpty(workingDirectories, "At least one workspace directory is required.", nameof(workingDirectories));

        List<AgentWorkspaceRoot> createdRoots = [];
        for (int i = 0; i < workingDirectories.Count; i++)
        {
            string directory = Path.GetFullPath(workingDirectories[i]);
            Directory.CreateDirectory(directory);

            createdRoots.Add(new()
            {
                Directory = Path.TrimEndingDirectorySeparator(directory),
            });
        }

        return createdRoots;
    }
}
