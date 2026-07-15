using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceContext(IReadOnlyList<string> workingDirectories)
{
    private const string NoWorkspaceRootsMessage = "At least one workspace directory is required.";

    private readonly Lock readFileHashesLock = new();
    private readonly Dictionary<string, ulong> readFileHashes = [with(StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<AgentWorkspaceRootDirectory> RootDirectories { get; } = CreateRootDirectories(workingDirectories);

    public bool TryGetShellWorkingDirectory([NotNullWhen(true)] out string? workingDirectory, [NotNullWhen(false)] out string? message)
    {
        workingDirectory = RootDirectories[0].FullPath;
        message = null;
        return true;
    }

    public MessageResult<AgentWorkspaceFile> ResolveWorkspaceFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "File path cannot be empty.";
        }

        string trimmedPath = path.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            return $"File path must be absolute: {path}";
        }

        if (!TryGetFullPath(trimmedPath, "File path", path, out string? fullPath, out string? message))
        {
            return message;
        }

        foreach (AgentWorkspaceRootDirectory rootDirectory in RootDirectories)
        {
            if (!rootDirectory.Contains(fullPath))
            {
                continue;
            }

            // We use relativePath variable below, so Path.IsEqual is not used here
            string relativePath = rootDirectory.GetRelativePath(fullPath);
            if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            {
                return "File path cannot target a workspace root.";
            }

            if (!TryValidateNoReparsePoint(rootDirectory, fullPath, out message))
            {
                return message;
            }

            return AgentWorkspaceFile.Create(rootDirectory, fullPath);
        }

        return $"File path must be under a workspace root: {path}";
    }

    public MessageResult<AgentWorkspaceDirectory> ResolveGlobPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspaceDirectoryFromRelativePath(RootDirectories[0], string.Empty);
        }

        string trimmedPath = path.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            return $"Directory path must be absolute: {path}";
        }

        return ResolveAbsoluteDirectoryPath(trimmedPath);
    }

    public MessageResult<AgentWorkspacePath> ResolveGrepPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return MessageResult<AgentWorkspacePath>.CastUp(CreateWorkspaceDirectoryFromRelativePath(RootDirectories[0], string.Empty));
        }

        string trimmedPath = path.Trim();
        if (Path.IsPathFullyQualified(trimmedPath))
        {
            return ResolveAbsoluteSearchPath(trimmedPath);
        }

        return $"Search path must be absolute: {path}";
    }

    public async Task MarkFileAsReadAsync(AgentWorkspaceFile file, CancellationToken cancellationToken = default)
    {
        ulong fileHash = await file.ComputeHashAsync(cancellationToken);

        lock (readFileHashesLock)
        {
            readFileHashes[file.FullPath] = fileHash;
        }
    }

    public async Task<bool> FileModifiedSinceLastReadAsync(AgentWorkspaceFile file, CancellationToken cancellationToken)
    {
        ulong readFileHash;
        lock (readFileHashesLock)
        {
            if (!readFileHashes.TryGetValue(file.FullPath, out readFileHash))
            {
                return true;
            }
        }

        return await file.ComputeHashAsync(cancellationToken) != readFileHash;
    }

    public static string? FindSimilarFile(AgentWorkspaceFile file)
    {
        try
        {
            string fileBaseName = file.NameWithoutExtension;
            foreach (AgentWorkspaceFile candidateFile in file.Directory.EnumerateFiles($"{fileBaseName}.*", false))
            {
                if (string.Equals(candidateFile.NameWithoutExtension, fileBaseName, StringComparison.OrdinalIgnoreCase) && !candidateFile.Equals(file))
                {
                    return candidateFile.Name;
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    public string? SuggestPathUnderWorkspaceRoots(AgentWorkspaceFile file)
    {
        foreach (AgentWorkspaceRootDirectory rootDirectory in RootDirectories)
        {
            if (SuggestPathUnderWorkspaceRoot(rootDirectory, file) is { } suggestedPath)
            {
                return suggestedPath;
            }
        }

        return null;
    }

    private static string? SuggestPathUnderWorkspaceRoot(AgentWorkspaceRootDirectory rootDirectory, AgentWorkspaceFile file)
    {
        // This method is used to suggest a path under the workspace root directory for a file that is under the parent of the workspace root directory.
        string? rootParent = Path.GetDirectoryName(rootDirectory.FullPath);

        // 1. The root directory is a root of the filesystem (e.g., C:\ or /), so we cannot suggest a path under it.
        // 2. The full path is not under the parent of the root directory, so we cannot suggest a path under it.
        // 3. The full path is already under the root directory, so we cannot suggest a path under it.
        if (string.IsNullOrEmpty(rootParent) || !Path.IsEqualOrSubdirectory(rootParent, file.FullPath) || rootDirectory.Contains(file.FullPath))
        {
            return null;
        }

        string relativePath = Path.GetRelativePath(rootParent, file.FullPath);
        string correctedPath = Path.GetFullPath(Path.Combine(rootDirectory.FullPath, relativePath));
        return File.Exists(correctedPath) ? correctedPath : null;
    }

    private MessageResult<AgentWorkspaceDirectory> ResolveAbsoluteDirectoryPath(string path)
    {
        if (!TryGetFullPath(path, "Directory path", path, out string? fullPath, out string? message))
        {
            return message;
        }

        foreach (AgentWorkspaceRootDirectory rootDirectory in RootDirectories)
        {
            if (!rootDirectory.Contains(fullPath))
            {
                continue;
            }

            if (!TryValidateNoReparsePoint(rootDirectory, fullPath, out message))
            {
                return message;
            }

            return AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
        }

        return $"Directory path must be under a workspace root: {path}";
    }

    private MessageResult<AgentWorkspacePath> ResolveAbsoluteSearchPath(string path)
    {
        if (!TryGetFullPath(path, "Search path", path, out string? fullPath, out string? message))
        {
            return message;
        }

        foreach (AgentWorkspaceRootDirectory rootDirectory in RootDirectories)
        {
            if (!rootDirectory.Contains(fullPath))
            {
                continue;
            }

            if (!TryValidateNoReparsePoint(rootDirectory, fullPath, out message))
            {
                return message;
            }

            AgentWorkspacePath searchPath = File.Exists(fullPath) ? AgentWorkspaceFile.Create(rootDirectory, fullPath) : AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
            return searchPath;
        }

        return $"Search path must be under a workspace root: {path}";
    }

    private MessageResult<AgentWorkspaceDirectory> CreateWorkspaceDirectoryFromRelativePath(AgentWorkspaceRootDirectory rootDirectory, string relativePath)
    {
        if (!TryResolveSafeFullPath(rootDirectory, relativePath, out string? fullPath, out string? message))
        {
            return message;
        }

        return AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
    }

    private static bool TryResolveSafeFullPath(AgentWorkspaceRootDirectory rootDirectory, string relativePath, [NotNullWhen(true)] out string? fullPath, [NotNullWhen(false)] out string? message)
    {
        string combinedPath;
        try
        {
            string localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            combinedPath = Path.Combine(rootDirectory.FullPath, localPath);
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            fullPath = null;
            message = $"Invalid path: '{relativePath}'. {ex.Message}";
            return false;
        }

        if (!TryGetFullPath(combinedPath, "Path", relativePath, out fullPath, out message))
        {
            return false;
        }

        if (!rootDirectory.Contains(fullPath))
        {
            fullPath = null;
            message = $"Invalid path: '{relativePath}'. The resolved path escapes the workspace root.";
            return false;
        }

        if (!TryValidateNoReparsePoint(rootDirectory, fullPath, out message))
        {
            fullPath = null;
            return false;
        }

        message = null;
        return true;
    }

    private static bool TryValidateNoReparsePoint(AgentWorkspaceRootDirectory rootDirectory, string fullPath, [NotNullWhen(false)] out string? message)
    {
        if (!TryContainsReparsePoint(rootDirectory, fullPath, out bool containsReparsePoint, out message))
        {
            return false;
        }

        if (containsReparsePoint)
        {
            message = "Invalid path: the resolved path contains a symbolic link or reparse point.";
            return false;
        }

        message = null;
        return true;
    }

    private static bool TryContainsReparsePoint(AgentWorkspaceRootDirectory rootDirectory, string fullPath, out bool containsReparsePoint, [NotNullWhen(false)] out string? message)
    {
        string root = rootDirectory.FullPath;
        string current = Path.TrimEndingDirectorySeparator(fullPath);
        containsReparsePoint = false;

        while (current.Length > root.Length)
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    containsReparsePoint = true;
                    message = null;
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                message = $"Unable to validate path: {fullPath}. {ex.Message}";
                return false;
            }

            string? parentPath = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parentPath))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parentPath);
        }

        message = null;
        return true;
    }

    private static bool TryGetFullPath(string path, string pathDescription, string originalPath, [NotNullWhen(true)] out string? fullPath, [NotNullWhen(false)] out string? message)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            message = null;
            return true;
        }
        catch (Exception ex) when (IsPathException(ex))
        {
            fullPath = null;
            message = $"{pathDescription} is invalid: {originalPath}. {ex.Message}";
            return false;
        }
    }

    private static List<AgentWorkspaceRootDirectory> CreateRootDirectories(IReadOnlyList<string> workingDirectories)
    {
        if (workingDirectories is null || workingDirectories.Count is 0)
        {
            throw new ArgumentException(NoWorkspaceRootsMessage, nameof(workingDirectories));
        }

        List<AgentWorkspaceRootDirectory> rootDirectories = [];
        for (int i = 0; i < workingDirectories.Count; i++)
        {
            string rawDirectory = workingDirectories[i];
            if (string.IsNullOrWhiteSpace(rawDirectory))
            {
                throw new ArgumentException("Workspace directory cannot be empty.", nameof(workingDirectories));
            }

            string directory = Path.GetFullPath(rawDirectory.Trim());
            Directory.CreateDirectory(directory);

            rootDirectories.Add(new(directory));
        }

        return rootDirectories;
    }

    private static bool IsPathException(Exception ex)
    {
        return ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException;
    }
}
