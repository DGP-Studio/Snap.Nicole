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

    public AgentWorkspaceSnapshot CreateSnapshot(Guid conversationId, AgentConversationWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        string memoryDirectory = GetMemoryDirectory(conversationId);
        Directory.CreateDirectory(memoryDirectory);

        if (workspace.Kind is AgentWorkspaceKind.ExternalFolder)
        {
            string externalFolderPath = NormalizeExternalFolderPath(workspace.ExternalFolderPath);
            return new()
            {
                Kind = AgentWorkspaceKind.ExternalFolder,
                WorkingDirectory = externalFolderPath,
                MemoryDirectory = memoryDirectory,
                ExternalFolderPath = externalFolderPath,
            };
        }

        string workingDirectory = GetAppManagedWorkingDirectory(conversationId);
        Directory.CreateDirectory(workingDirectory);

        return new()
        {
            Kind = AgentWorkspaceKind.AppManaged,
            WorkingDirectory = workingDirectory,
            MemoryDirectory = memoryDirectory,
        };
    }

    public string GetAppManagedWorkingDirectory(Guid conversationId)
    {
        return Path.Combine(GetAppManagedWorkspaceRoot(conversationId), WorkingDirectoryName);
    }

    public string GetMemoryDirectory(Guid conversationId)
    {
        return Path.Combine(GetAppManagedWorkspaceRoot(conversationId), MemoryDirectoryName);
    }

    public string NormalizeExternalFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DirectoryNotFoundException("External workspace folder is not configured.");
        }

        string fullPath = Path.GetFullPath(path.Trim());
        DirectoryInfo directory = new(fullPath);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"External workspace folder does not exist: {fullPath}");
        }

        if ((directory.Attributes & FileAttributes.Directory) == 0)
        {
            throw new IOException($"External workspace path is not a directory: {fullPath}");
        }

        EnsureDirectoryAccessible(fullPath);
        return fullPath;
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
        return Path.Combine(WellKnownLocations.Cache, AgentWorkspacesDirectoryName, conversationId.ToString("N"));
    }

    private static void EnsureDirectoryAccessible(string path)
    {
        using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        _ = enumerator.MoveNext();
    }
}
