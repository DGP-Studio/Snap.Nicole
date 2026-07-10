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
    private const string ReparsePointMessage = "Invalid path: the resolved path contains a symbolic link or reparse point.";

    private readonly Lock readFileHashesLock = new();
    private readonly Dictionary<string, ulong> readFileHashes = [with(StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<string> RootDirectories { get; } = CreateRootDirectories(workingDirectories);

    public bool TryGetShellWorkingDirectory([NotNullWhen(true)] out string? workingDirectory, [NotNullWhen(false)] out string? message)
    {
        workingDirectory = RootDirectories[0];
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

        foreach (string rootDirectory in RootDirectories)
        {
            if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
            {
                continue;
            }

            // We use relativePath variable below, so Path.IsEqual is not used here
            string relativePath = AgentWorkspacePath.GetRelativePath(rootDirectory, fullPath);
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

    public MessageResult<AgentWorkspaceDirectory> ResolveGlobDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspaceDirectoryFromRelativePath(RootDirectories[0], string.Empty);
        }

        string trimmedPath = path.Trim();
        if (Path.IsPathFullyQualified(trimmedPath))
        {
            return ResolveAbsoluteDirectoryPath(trimmedPath);
        }

        return $"Directory path must be absolute: {path}";
    }

    public MessageResult<AgentWorkspacePath> ResolveGrepSearchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageResult<AgentWorkspaceDirectory> directoryResult = CreateWorkspaceDirectoryFromRelativePath(RootDirectories[0], string.Empty);
            if (directoryResult is string message)
            {
                return message;
            }

            if (directoryResult is not AgentWorkspaceDirectory directory)
            {
                throw new InvalidOperationException("Unexpected result from CreateWorkspaceDirectoryFromRelativePath.");
            }

            return directory;
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

    public static string? FindSimilarFile(string fullPath)
    {
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        try
        {
            string fileBaseName = Path.GetFileNameWithoutExtension(fullPath);
            foreach (string candidatePath in Directory.EnumerateFiles(directory, $"{fileBaseName}.*"))
            {
                string candidateFileName = Path.GetFileName(candidatePath);

                if (string.Equals(Path.GetFileNameWithoutExtension(candidateFileName), fileBaseName, StringComparison.OrdinalIgnoreCase) &&
                    !Path.IsEqual(candidatePath, fullPath))
                {
                    return candidateFileName;
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    public string? SuggestPathUnderWorkspaceRoots(string fullPath)
    {
        foreach (string rootDirectory in RootDirectories)
        {
            if (SuggestPathUnderWorkspaceRoot(rootDirectory, fullPath) is { } suggestedPath)
            {
                return suggestedPath;
            }
        }

        return null;
    }

    private static string? SuggestPathUnderWorkspaceRoot(string rootDirectory, string fullPath)
    {
        string? rootParent = Path.GetDirectoryName(rootDirectory);

        // 1. The root directory is a root of the filesystem (e.g., C:\ or /), so we cannot suggest a path under it.
        // 2. The full path is not under the parent of the root directory, so we cannot suggest a path under the root directory.
        // 3. The full path is already under the root directory, so we cannot suggest a path under it.
        if (string.IsNullOrEmpty(rootParent) || !Path.IsEqualOrSubdirectory(rootParent, fullPath) || Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
        {
            return null;
        }

        string relativePath = Path.GetRelativePath(rootParent, fullPath);
        string correctedPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        return File.Exists(correctedPath) ? correctedPath : null;
    }

    private MessageResult<AgentWorkspaceDirectory> ResolveAbsoluteDirectoryPath(string path)
    {
        if (!TryGetFullPath(path, "Directory path", path, out string? fullPath, out string? message))
        {
            return message;
        }

        foreach (string rootDirectory in RootDirectories)
        {
            if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
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

        foreach (string rootDirectory in RootDirectories)
        {
            if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
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

    private MessageResult<AgentWorkspaceDirectory> CreateWorkspaceDirectoryFromRelativePath(string rootDirectory, string relativePath)
    {
        if (!TryResolveSafeFullPath(rootDirectory, relativePath, out string? fullPath, out string? message))
        {
            return message;
        }

        return AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
    }

    private static bool TryResolveSafeFullPath(string rootDirectory, string relativePath, [NotNullWhen(true)] out string? fullPath, [NotNullWhen(false)] out string? message)
    {
        string combinedPath;
        try
        {
            string localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            combinedPath = Path.Combine(rootDirectory, localPath);
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

        if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
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

    private static bool TryValidateNoReparsePoint(string rootDirectory, string fullPath, [NotNullWhen(false)] out string? message)
    {
        if (!TryContainsReparsePoint(rootDirectory, fullPath, out bool containsReparsePoint, out message))
        {
            return false;
        }

        if (containsReparsePoint)
        {
            message = ReparsePointMessage;
            return false;
        }

        message = null;
        return true;
    }

    private static bool TryContainsReparsePoint(string rootDirectory, string fullPath, out bool containsReparsePoint, [NotNullWhen(false)] out string? message)
    {
        string root = Path.TrimEndingDirectorySeparator(rootDirectory);
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

    private static List<string> CreateRootDirectories(IReadOnlyList<string> workingDirectories)
    {
        if (workingDirectories is null || workingDirectories.Count is 0)
        {
            throw new ArgumentException(NoWorkspaceRootsMessage, nameof(workingDirectories));
        }

        List<string> rootDirectories = [];
        for (int i = 0; i < workingDirectories.Count; i++)
        {
            string rawDirectory = workingDirectories[i];
            if (string.IsNullOrWhiteSpace(rawDirectory))
            {
                throw new ArgumentException("Workspace directory cannot be empty.", nameof(workingDirectories));
            }

            string directory = Path.GetFullPath(rawDirectory.Trim());
            Directory.CreateDirectory(directory);

            rootDirectories.Add(Path.TrimEndingDirectorySeparator(directory));
        }

        return rootDirectories;
    }

    private static bool IsPathException(Exception ex)
    {
        return ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException;
    }
}
