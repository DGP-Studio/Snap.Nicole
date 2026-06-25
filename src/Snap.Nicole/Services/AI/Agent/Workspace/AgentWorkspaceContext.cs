using Snap.Nicole.Core;
using Snap.Nicole.Core.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Hashing;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceContext(IReadOnlyList<string> workingDirectories)
{
    private const int FileHashBufferSize = 81920;

    private readonly Lock readFileHashesLock = new();
    private readonly Dictionary<string, ulong> readFileHashes = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> RootDirectories { get; } = CreateRootDirectories(workingDirectories);

#if NET11_0
#error Try union for multiple out parameters
#endif
    public bool TryResolveWorkspaceFile(string path, [NotNullWhen(true)] out AgentWorkspaceFile? file, [NotNullWhen(false)] out string? message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            file = null;
            message = "File path cannot be empty.";
            return false;
        }

        string trimmedPath = path.Trim();
        if (!Path.IsPathFullyQualified(trimmedPath))
        {
            file = null;
            message = $"File path must be absolute: {path}";
            return false;
        }

        string fullPath = Path.GetFullPath(trimmedPath);
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
                file = null;
                message = "File path cannot target a workspace root.";
                return false;
            }

            if (ContainsReparsePoint(rootDirectory, fullPath))
            {
                file = null;
                message = "Invalid path: the resolved path contains a symbolic link or reparse point.";
                return false;
            }

            file = AgentWorkspaceFile.Create(rootDirectory, fullPath);
            message = null;
            return true;
        }

        file = null;
        message = $"File path must be under a workspace root: {path}";
        return false;
    }

    public AgentWorkspaceDirectory ResolveGlobDirectoryPath(string? path)
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

        throw new ArgumentException($"Directory path must be absolute: {path}", nameof(path));
    }

    public AgentWorkspacePath ResolveGrepSearchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateWorkspaceDirectoryFromRelativePath(RootDirectories[0], string.Empty);
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

    public void MarkFileAsRead(string fullPath, ulong fileHash)
    {
        lock (readFileHashesLock)
        {
            readFileHashes[fullPath] = fileHash;
        }
    }

    public void InvalidateFileRead(string fullPath)
    {
        lock (readFileHashesLock)
        {
            readFileHashes.Remove(fullPath);
        }
    }

    public async Task<bool> FileModifiedSinceLastReadAsync(string fullPath, CancellationToken cancellationToken)
    {
        ulong readFileHash;
        lock (readFileHashesLock)
        {
            if (!readFileHashes.TryGetValue(fullPath, out readFileHash))
            {
                return true;
            }
        }

        return await ComputeFileHashAsync(fullPath, cancellationToken) != readFileHash;
    }

    [Obsolete]
    public async Task ValidateFileReadForModificationAsync(string fullPath, CancellationToken cancellationToken)
    {
        ulong readFileHash;
        lock (readFileHashesLock)
        {
            if (!readFileHashes.TryGetValue(fullPath, out readFileHash))
            {
                throw new InvalidOperationException($"File '{fullPath}' must be read before modifying.");
            }
        }

        ulong currentFileHash = await ComputeFileHashAsync(fullPath, cancellationToken);
        if (currentFileHash != readFileHash)
        {
            throw new InvalidOperationException($"File '{fullPath}' content changed after it was read. Read it again before modifying.");
        }
    }

    public static async Task<ulong> ComputeFileHashAsync(string fullPath, CancellationToken cancellationToken)
    {
        XxHash64 hash = new();
        using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileHashBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await hash.AppendAsync(stream, cancellationToken);
        return hash.GetCurrentHashAsUInt64();
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

    private AgentWorkspaceDirectory ResolveAbsoluteDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (string rootDirectory in RootDirectories)
        {
            if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
            {
                continue;
            }

            ThrowIfContainsReparsePoint(rootDirectory, fullPath);
            return AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
        }

        throw new ArgumentException($"Directory path must be under a workspace root: {path}", nameof(path));
    }

    private AgentWorkspacePath ResolveAbsoluteSearchPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (string rootDirectory in RootDirectories)
        {
            if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
            {
                continue;
            }

            ThrowIfContainsReparsePoint(rootDirectory, fullPath);
            return File.Exists(fullPath) ? AgentWorkspaceFile.Create(rootDirectory, fullPath) : AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
        }

        throw new ArgumentException($"Search path must be under a workspace root: {path}", nameof(path));
    }

    private AgentWorkspaceDirectory CreateWorkspaceDirectoryFromRelativePath(string rootDirectory, string relativePath)
    {
        string fullPath = ResolveSafeFullPath(rootDirectory, relativePath);
        return AgentWorkspaceDirectory.Create(rootDirectory, fullPath);
    }

    private string ResolveSafeFullPath(string rootDirectory, string relativePath)
    {
        string localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, localPath));
        if (!Path.IsEqualOrSubdirectory(rootDirectory, fullPath))
        {
            throw new ArgumentException($"Invalid path: '{relativePath}'. The resolved path escapes the workspace root.", nameof(relativePath));
        }

        ThrowIfContainsReparsePoint(rootDirectory, fullPath);
        return fullPath;
    }

    private static bool ContainsReparsePoint(string rootDirectory, string fullPath)
    {
        string root = Path.TrimEndingDirectorySeparator(rootDirectory);
        string current = Path.TrimEndingDirectorySeparator(fullPath);

        while (current.Length > root.Length)
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                    //throw new ArgumentException("Invalid path: the resolved path contains a symbolic link or reparse point.");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            string? parentPath = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parentPath))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parentPath);
        }

        return false;
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

    private static List<string> CreateRootDirectories(IReadOnlyList<string> workingDirectories)
    {
        ArgumentNullException.ThrowIfNull(workingDirectories);
        ArgumentException.ThrowIfEmpty(workingDirectories, "At least one workspace directory is required.", nameof(workingDirectories));

        List<string> rootDirectories = [];
        for (int i = 0; i < workingDirectories.Count; i++)
        {
            string directory = Path.GetFullPath(workingDirectories[i]);
            Directory.CreateDirectory(directory);

            rootDirectories.Add(Path.TrimEndingDirectorySeparator(directory));
        }

        return rootDirectories;
    }
}
