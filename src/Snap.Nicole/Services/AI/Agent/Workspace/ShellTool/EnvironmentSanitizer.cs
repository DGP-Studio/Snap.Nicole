using System.Collections.Generic;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal static class EnvironmentSanitizer
{
    public static readonly IReadOnlyList<string> PreservedVariables = ["PATH", "HOME", "USER", "USERNAME", "USERPROFILE", "SystemRoot", "TEMP", "TMP"];

    public static void RemoveNonPreserved(IDictionary<string, string?> environment)
    {
        if (environment is null)
        {
            return;
        }

        Dictionary<string, string?> keep = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (string name in PreservedVariables)
        {
            if (environment.TryGetValue(name, out string? v) && v is not null)
            {
                keep[name] = v;
            }
        }

        environment.Clear();
        foreach (KeyValuePair<string, string?> kv in keep)
        {
            environment[kv.Key] = kv.Value;
        }
    }
}