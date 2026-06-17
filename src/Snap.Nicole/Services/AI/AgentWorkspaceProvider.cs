using Snap.Nicole.Core.Diagnostics;
using Snap.Nicole.Core.IO;
using Snap.Nicole.Services.AI.Models;
using System.Collections.Generic;
using System.IO;

namespace Snap.Nicole.Services.AI;

internal sealed class AgentWorkspaceProvider
{
    private const string AgentWorkspacesDirectoryName = "AgentWorkspaces";
    private const string WorkingDirectoryName = "working";
    private const string MemoryDirectoryName = "memory";

    public AgentWorkspaceSnapshot CreateSnapshot(Guid conversationId, IReadOnlyList<string> externalDirectories)
    {
        ArgumentNullException.ThrowIfNull(externalDirectories);

        string memoryDirectory = GetMemoryDirectory();
        Directory.CreateDirectory(memoryDirectory);

        string workingDirectory = GetAppManagedWorkingDirectory(conversationId);
        Directory.CreateDirectory(workingDirectory);

        IReadOnlyList<string> normalizedExternalDirectories = externalDirectories.Count > 0 ? NormalizeExternalDirectories(externalDirectories) : [];
        return new()
        {
            WorkingDirectory = workingDirectory,
            WorkingDirectories = [workingDirectory, .. normalizedExternalDirectories],
            MemoryDirectory = memoryDirectory,
        };
    }

    public string GetAppManagedWorkingDirectory(Guid conversationId)
    {
        return Path.Combine(GetAppManagedWorkspaceRoot(conversationId), WorkingDirectoryName);
    }

    public string GetMemoryDirectory()
    {
        return Path.Combine(GetAppManagedWorkspacesRoot(), MemoryDirectoryName);
    }

    public string NormalizeExternalDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DirectoryNotFoundException("External workspace directory is not configured.");
        }

        string fullPath = Path.GetFullPath(path.Trim());
        DirectoryInfo directory = new(fullPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"External workspace directory does not exist: {fullPath}");
        }

        if ((directory.Attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException($"External workspace path is not a directory: {fullPath}");
        }

        EnsureDirectoryAccessible(fullPath);
        return fullPath;
    }

    public IReadOnlyList<string> NormalizeExternalDirectories(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<string> normalizedPaths = [];
        HashSet<string> seenPaths = new();
        foreach (string path in paths)
        {
            string normalizedPath = NormalizeExternalDirectory(path);
            if (seenPaths.Add(normalizedPath))
            {
                normalizedPaths.Add(normalizedPath);
            }
        }

        if (normalizedPaths.Count is 0)
        {
            throw new DirectoryNotFoundException("External workspace directory is not configured.");
        }

        return normalizedPaths;
    }

    public void DeleteAppManagedWorkspace(Guid conversationId)
    {
        string directory = GetAppManagedWorkspaceRoot(conversationId);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex) when (IsWorkspaceException(ex))
        {
            using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.AgentWorkspaceDelete, "Delete app-managed agent workspace");
            span.SetData(SentryData.AgentWorkspaceDirectory, directory);
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.AgentWorkspaceDelete);
        }
    }

    public static bool IsWorkspaceException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    }

    private static string GetAppManagedWorkspaceRoot(Guid conversationId)
    {
        return Path.Combine(GetAppManagedWorkspacesRoot(), conversationId.ToString("N"));
    }

    private static string GetAppManagedWorkspacesRoot()
    {
        return Path.Combine(WellKnownLocations.Cache, AgentWorkspacesDirectoryName);
    }

    private static void EnsureDirectoryAccessible(string path)
    {
        using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        _ = enumerator.MoveNext();
    }
}
