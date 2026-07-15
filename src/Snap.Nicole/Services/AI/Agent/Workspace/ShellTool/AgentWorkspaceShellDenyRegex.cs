using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal static partial class AgentWorkspaceShellDenyRegex
{
    private static IReadOnlyList<Regex>? regexList;

    [GeneratedRegex(@"(^|[;&|]\s*)rm\b(?=.*\s-rf\b|.*\s-fr\b|.*\s-r\b.*\s-f\b|.*\s-f\b.*\s-r\b)\s+[/\\]?(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DenyRegexRm { get; }

    [GeneratedRegex(@"(^|[;&|]\s*)Remove-Item\b(?=.*\b(-Recurse|-r)\b)(?=.*\b(-Force|-fo)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DenyRegexRemoveItem { get; }

    [GeneratedRegex(@"(^|[;&|]\s*)format(\.com)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DenyRegexFormat { get; }

    public static IReadOnlyList<Regex> GetDenyRegexList()
    {
        return regexList ??= [DenyRegexRm, DenyRegexRemoveItem, DenyRegexFormat];
    }
}