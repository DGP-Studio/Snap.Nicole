using System.IO;
using System.Runtime.InteropServices;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal static class AgentWorkspaceShellResolver
{
    private const string EnvironmentVariableName = "AGENT_FRAMEWORK_SHELL";

    public static AgentWorkspaceResolvedShell Resolve()
    {
        string? overrideShell = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(overrideShell))
        {
            return ClassifyExplicit(overrideShell);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (TryFindOnPath("pwsh", out string pwshPath))
            {
                return new(pwshPath, AgentWorkspaceShellKind.PowerShell);
            }

            if (TryFindOnPath("powershell", out string powershellPath))
            {
                return new(powershellPath, AgentWorkspaceShellKind.PowerShell);
            }

            return new(Path.Combine(SystemRoot(), "System32", "cmd.exe"), AgentWorkspaceShellKind.Cmd);
        }

        if (File.Exists("/bin/bash"))
        {
            return new("/bin/bash", AgentWorkspaceShellKind.Bash);
        }

        return new("/bin/sh", AgentWorkspaceShellKind.Sh);
    }

    private static AgentWorkspaceResolvedShell ClassifyExplicit(string path)
    {
        return new(path, ClassifyKind(path));
    }

    private static AgentWorkspaceShellKind ClassifyKind(string path)
    {
        return Path.GetFileNameWithoutExtension(path).ToUpperInvariant() switch
        {
            "PWSH" or "POWERSHELL" => AgentWorkspaceShellKind.PowerShell,
            "CMD" => AgentWorkspaceShellKind.Cmd,
            "BASH" => AgentWorkspaceShellKind.Bash,
            _ => AgentWorkspaceShellKind.Sh,
        };
    }

    private static bool TryFindOnPath(string name, out string fullPath)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            fullPath = string.Empty;
            return false;
        }

        string[] extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? [".exe", ".cmd", ".bat", string.Empty] : [string.Empty];
        string[] directories = path.Split(Path.PathSeparator);
        foreach (string directory in directories)
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            foreach (string extension in extensions)
            {
                string candidatePath = Path.Combine(directory, name + extension);
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

    private static string SystemRoot()
    {
        return Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
    }
}
