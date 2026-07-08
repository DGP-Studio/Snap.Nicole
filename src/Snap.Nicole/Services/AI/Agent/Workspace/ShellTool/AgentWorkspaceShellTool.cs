using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal sealed class AgentWorkspaceShellTool(AgentWorkspaceContext context) : AgentWorkspaceTool(context)
{
    private const int MaxOutputBytes = 65536;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex[] DenyRegexes =
    [
        new(@"(^|[;&|]\s*)rm\b(?=.*\s-rf\b|.*\s-fr\b|.*\s-r\b.*\s-f\b|.*\s-f\b.*\s-r\b)\s+[/\\]?(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"(^|[;&|]\s*)Remove-Item\b(?=.*\b(-Recurse|-r)\b)(?=.*\b(-Force|-fo)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
        new(@"(^|[;&|]\s*)format(\.com)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant),
    ];

    private readonly AgentWorkspaceResolvedShell shell = AgentWorkspaceShellResolver.Resolve();

    public override AITool Tool { get => field ??= AIFunctionFactory.Create(InvokeAsync).AsApprovalRequired(); }

    [DisplayName(Prompt.ShellToolName)]
    [Description(Prompt.ShellToolDescription)]
    public Task<string> InvokeAsync(
        [Description("The shell command to execute. Each call starts a fresh shell, so command state does not persist.")] string command,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(command, cancellationToken);
    }

    private async Task<string> RunAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (CreatePolicyRejectionMessage(command) is { } rejectionMessage)
        {
            return rejectionMessage;
        }

        if (!Context.TryGetShellWorkingDirectory(out string? workingDirectory, out string? message))
        {
            return message;
        }

        AgentWorkspaceShellResult result = await RunProcessAsync(command, workingDirectory, cancellationToken);
        return result.FormatForModel();
    }

    private async Task<AgentWorkspaceShellResult> RunProcessAsync(string command, string workingDirectory, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = shell.Binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        foreach (string argument in shell.CreateArguments(command))
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (shell.Kind is AgentWorkspaceShellKind.PowerShell)
        {
            startInfo.Environment["PSDefaultParameterValues"] = "Out-File:Encoding=utf8";
        }

        using Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        AgentWorkspaceShellOutputBuffer stdoutBuffer = new(MaxOutputBytes);
        AgentWorkspaceShellOutputBuffer stderrBuffer = new(MaxOutputBytes);
        process.OutputDataReceived += delegate(object _, DataReceivedEventArgs args)
        {
            if (args.Data is not null)
            {
                stdoutBuffer.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs args)
        {
            if (args.Data is not null)
            {
                stderrBuffer.AppendLine(args.Data);
            }
        };

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new IOException($"Failed to launch shell '{shell.Binary}': {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool timedOut = false;
        using CancellationTokenSource timeoutCts = new(DefaultTimeout);
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        if (timedOut)
        {
            KillProcessTree(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
            }
        }

        stopwatch.Stop();
        process.WaitForExit();
        var (stdout, stdoutTruncated) = stdoutBuffer.ToFinalString();
        var (stderr, stderrTruncated) = stderrBuffer.ToFinalString();
        return new(stdout, stderr, timedOut ? 124 : process.ExitCode, stopwatch.Elapsed, stdoutTruncated || stderrTruncated, timedOut);
    }

    private static string? CreatePolicyRejectionMessage(string command)
    {
        string trimmedCommand = command.Trim();
        if (trimmedCommand.Length is 0)
        {
            return "Command rejected by policy: empty command";
        }

        foreach (Regex denyRegex in DenyRegexes)
        {
            if (denyRegex.IsMatch(trimmedCommand))
            {
                return $"Command rejected by policy: matched deny pattern: {denyRegex}";
            }
        }

        return null;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}
