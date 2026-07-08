using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal readonly struct AgentWorkspaceResolvedShell
{
    public AgentWorkspaceResolvedShell(string binary, AgentWorkspaceShellKind kind)
    {
        Binary = binary;
        Kind = kind;
    }

    public string Binary { get; }

    public AgentWorkspaceShellKind Kind { get; }

    public IReadOnlyList<string> CreateArguments(string command)
    {
        return Kind switch
        {
            AgentWorkspaceShellKind.PowerShell => ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", command],
            AgentWorkspaceShellKind.Cmd => ["/d", "/c", command],
            AgentWorkspaceShellKind.Sh => ["-c", command],
            _ => ["--noprofile", "--norc", "-c", command],
        };
    }
}
