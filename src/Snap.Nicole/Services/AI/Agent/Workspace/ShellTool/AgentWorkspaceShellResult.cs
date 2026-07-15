using System.Text;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal sealed class AgentWorkspaceShellResult(string stdout, string stderr, int exitCode, TimeSpan duration, bool truncated, bool timedOut)
{
    public string Stdout { get; } = stdout;

    public string Stderr { get; } = stderr;

    public int ExitCode { get; } = exitCode;

    public TimeSpan Duration { get; } = duration;

    public bool Truncated { get; } = truncated;

    public bool TimedOut { get; } = timedOut;

    public string FormatForModel()
    {
        StringBuilder builder = new();
        if (!string.IsNullOrEmpty(Stdout))
        {
            builder.Append(Stdout);
            if (Truncated)
            {
                builder.AppendLine().Append("[stdout truncated]");
            }

            builder.AppendLine();
        }

        if (!string.IsNullOrEmpty(Stderr))
        {
            builder.Append("stderr: ").Append(Stderr).AppendLine();
        }

        if (TimedOut)
        {
            builder.AppendLine("[command timed out]");
        }

        builder.Append("exit_code: ").Append(ExitCode);
        return builder.ToString();
    }
}
