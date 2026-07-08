using System.Text;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal sealed class AgentWorkspaceShellResult
{
    public AgentWorkspaceShellResult(string stdout, string stderr, int exitCode, TimeSpan duration, bool truncated, bool timedOut)
    {
        Stdout = stdout;
        Stderr = stderr;
        ExitCode = exitCode;
        Duration = duration;
        Truncated = truncated;
        TimedOut = timedOut;
    }

    public string Stdout { get; }

    public string Stderr { get; }

    public int ExitCode { get; }

    public TimeSpan Duration { get; }

    public bool Truncated { get; }

    public bool TimedOut { get; }

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
