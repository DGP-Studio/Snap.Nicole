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

        return new(Path.Combine(SystemRoot(), "System32", "cmd.exe"), AgentWorkspaceShellKind.Cmd);
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
            _ => throw new InvalidOperationException($"The {EnvironmentVariableName} environment variable supports only PowerShell and CMD."),
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
