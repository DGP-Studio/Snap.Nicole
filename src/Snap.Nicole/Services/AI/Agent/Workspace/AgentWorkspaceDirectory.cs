using System.Collections.Generic;
using System.IO;

namespace Snap.Nicole.Services.AI.Agent.Workspace;

internal sealed class AgentWorkspaceDirectory : AgentWorkspacePath
{
    public bool Exists { get => Directory.Exists(FullPath); }

    public string Name { get => Path.GetFileName(Path.TrimEndingDirectorySeparator(FullPath)); }

    public static AgentWorkspaceDirectory Create(AgentWorkspaceRootDirectory rootDirectory, string fullPath)
    {
        return new()
        {
            RootDirectory = rootDirectory,
            FullPath = fullPath,
        };
    }

    public IEnumerable<AgentWorkspaceFile> EnumerateFiles(string searchPattern, bool recurseSubdirectories)
    {
        EnumerationOptions enumerationOptions = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = recurseSubdirectories,
        };

        foreach (string path in Directory.EnumerateFiles(FullPath, searchPattern, enumerationOptions))
        {
            yield return AgentWorkspaceFile.Create(RootDirectory, Path.GetFullPath(path));
        }
    }

    public IEnumerable<AgentWorkspaceDirectory> EnumerateDirectories(string searchPattern, bool recurseSubdirectories)
    {
        EnumerationOptions enumerationOptions = new()
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = recurseSubdirectories,
        };

        foreach (string path in Directory.EnumerateDirectories(FullPath, searchPattern, enumerationOptions))
        {
            yield return Create(RootDirectory, Path.GetFullPath(path));
        }
    }

    public void CreateDirectory()
    {
        Directory.CreateDirectory(FullPath);
    }
}
