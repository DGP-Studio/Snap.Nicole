using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal readonly struct AgentWorkspaceResolvedShell(string binary, AgentWorkspaceShellKind kind)
{
    public string Binary { get; } = binary;

    public AgentWorkspaceShellKind Kind { get; } = kind;

    public IReadOnlyList<string> CreateArguments(string command)
    {
        return Kind switch
        {
            AgentWorkspaceShellKind.PowerShell => ["-NoProfile", "-NoLogo", "-NonInteractive", "-Command", command],
            AgentWorkspaceShellKind.Cmd => ["/d", "/c", command],
            _ => throw new NotSupportedException($"Unsupported shell kind: {Kind}"),
        };
    }
}
