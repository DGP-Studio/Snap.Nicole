using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal static class AgentWorkspaceShellResolver
{
    private static readonly IReadOnlyList<string> Extensions = [".exe", ".cmd", ".bat", string.Empty];

    public static AgentWorkspaceResolvedShell Resolve()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("ShellTool supports only Windows PowerShell and CMD.");
        }

        if (TryFindOnPath("pwsh", out string pwshPath))
        {
            return new(pwshPath, AgentWorkspaceShellKind.PowerShell);
        }

        if (TryFindOnPath("powershell", out string powershellPath))
        {
            return new(powershellPath, AgentWorkspaceShellKind.PowerShell);
        }

        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        return new(Path.Combine(systemRoot, "System32", "cmd.exe"), AgentWorkspaceShellKind.Cmd);
    }

    private static bool TryFindOnPath(string name, out string fullPath)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            fullPath = string.Empty;
            return false;
        }

        string[] directories = path.Split(Path.PathSeparator);
        foreach (string directory in directories)
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            foreach (string extension in Extensions)
            {
                string candidatePath = Path.Combine(directory, $"{name}{extension}");
                if (File.Exists(candidatePath))
                {
                    fullPath = candidatePath;
                    return true;
                }
            }
        }

        fullPath = string.Empty;
        return false;
    }
}
