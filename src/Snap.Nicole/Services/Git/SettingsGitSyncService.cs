using Snap.Nicole.Core.IO;
using Sentry;
using Snap.Nicole.Core.Diagnostics;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Snap.Nicole.Services.Git;

internal sealed class SettingsGitSyncService : ISettingsGitSyncService
{
    private const string GitFileName = "git";
    private const string RemoteName = "origin";
    private const string DefaultBranchName = "main";
    private const string CommitAuthorName = "Snap Nicole";
    private const string CommitAuthorEmail = "snap-nicole@local";

    private static readonly string[] BlockingFiles = ["CHERRY_PICK_HEAD", "MERGE_HEAD", "REBASE_HEAD", "REVERT_HEAD", "index.lock"];
    private static readonly string[] GetUpstreamArguments = ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"];

    public string RepositoryPath { get => WellKnownLocations.Settings; }

    public async Task<SettingsGitRepositoryState> GetRepositoryStateAsync(CancellationToken cancellationToken = default)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsGitState, "Get settings Git repository state");

        try
        {
            return CompleteRepositoryStateSpan(span, await GetRepositoryStateCoreAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsGitState);
            throw;
        }
    }

    public async Task<SettingsGitOperationResult> InitializeRepositoryAsync(string remoteUrl, CancellationToken cancellationToken = default)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsGitInitialize, "Initialize settings Git repository");

        try
        {
            return CompleteOperationSpan(span, await InitializeRepositoryCoreAsync(remoteUrl, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsGitInitialize);
            throw;
        }
    }

    public async Task<SettingsGitOperationResult> PullAsync(CancellationToken cancellationToken = default)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsGitPull, "Pull settings Git repository");

        try
        {
            return CompleteOperationSpan(span, await PullCoreAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsGitPull);
            throw;
        }
    }

    private async Task<SettingsGitOperationResult> PullCoreAsync(CancellationToken cancellationToken)
    {
        if (await EnsureRepositoryReadyAsync(cancellationToken) is { Succeeded: false } ready)
        {
            return ready;
        }

        if (EnsureNoRepositoryOperationInProgress() is { Succeeded: false } repositoryOperation)
        {
            return repositoryOperation;
        }

        if (await CommitSettingsChangesAsync("Save settings before pull", cancellationToken) is { Succeeded: false } commit)
        {
            return commit;
        }

        SettingsGitCommandResult remoteHeads = await RunGitAsync(["ls-remote", "--heads", RemoteName], RepositoryPath, cancellationToken);
        if (!remoteHeads.Succeeded)
        {
            return SettingsGitOperationResult.CommandFailure(remoteHeads);
        }

        if (string.IsNullOrWhiteSpace(remoteHeads.Output))
        {
            return SettingsGitOperationResult.Success(SettingsGitOperationDetailKind.RemoteEmpty);
        }

        if (await PullWithMergeAsync(cancellationToken) is { Succeeded: true } pull)
        {
            return SettingsGitOperationResult.Command(pull);
        }

        return await ForcePullAsync(cancellationToken);
    }

    public async Task<SettingsGitOperationResult> PushAsync(CancellationToken cancellationToken = default)
    {
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsGitPush, "Push settings Git repository");

        try
        {
            return CompleteOperationSpan(span, await PushCoreAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            SentryDiagnostics.CaptureException(ex, span, SentryOperations.SettingsGitPush);
            throw;
        }
    }

    private static SettingsGitRepositoryState CompleteRepositoryStateSpan(SentryDiagnosticSpan span, SettingsGitRepositoryState result)
    {
        span.SetTag(SentryTags.SettingsGitAvailable, result.IsGitAvailable);
        span.SetTag(SentryTags.SettingsGitRepository, result.IsRepository);
        span.SetTag(SentryTags.SettingsGitHasRemote, !string.IsNullOrWhiteSpace(result.RemoteUrl));
        span.SetTag(SentryTags.SettingsGitFailureKind, result.FailureKind.ToString());
        span.Finish(result.FailureKind is SettingsGitFailureKind.None ? SpanStatus.Ok : GetFailureSpanStatus(result.FailureKind));

        return result;
    }

    private static SettingsGitOperationResult CompleteOperationSpan(SentryDiagnosticSpan span, SettingsGitOperationResult result)
    {
        span.SetTag(SentryTags.SettingsGitSucceeded, result.Succeeded);
        span.SetTag(SentryTags.SettingsGitFailureKind, result.FailureKind.ToString());
        span.Finish(result.Succeeded ? SpanStatus.Ok : GetFailureSpanStatus(result.FailureKind));

        return result;
    }

    private static SettingsGitCommandResult CompleteCommandSpan(SentryDiagnosticSpan span, SettingsGitCommandResult result)
    {
        span.SetTag(SentryTags.SettingsGitCommandSucceeded, result.Succeeded);
        span.SetTag(SentryTags.SettingsGitFailureKind, result.Succeeded ? SettingsGitFailureKind.None.ToString() : result.FailureKind.ToString());
        span.SetData(SentryData.SettingsGitExitCode, result.ExitCode);
        span.Finish(result.Succeeded ? SpanStatus.Ok : GetFailureSpanStatus(result.FailureKind));

        return result;
    }

    private static SpanStatus GetFailureSpanStatus(SettingsGitFailureKind failureKind)
    {
        return failureKind switch
        {
            SettingsGitFailureKind.GitUnavailable => SpanStatus.NotFound,
            SettingsGitFailureKind.Network => SpanStatus.Unavailable,
            SettingsGitFailureKind.Permission => SpanStatus.PermissionDenied,
            SettingsGitFailureKind.Remote => SpanStatus.FailedPrecondition,
            SettingsGitFailureKind.Repository => SpanStatus.FailedPrecondition,
            SettingsGitFailureKind.Conflict => SpanStatus.Aborted,
            SettingsGitFailureKind.CommandFailed => SpanStatus.InternalError,
            _ => SpanStatus.FailedPrecondition,
        };
    }

    private static ReadOnlySpan<char> GetBranchNameFromRemoteReference(ReadOnlySpan<char> remoteReference)
    {
        int separatorIndex = remoteReference.IndexOf('/');
        return separatorIndex >= 0 ? remoteReference[(separatorIndex + 1)..] : remoteReference;
    }

    private static string GetGitCommandName(string[] arguments)
    {
        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            // Skip configuration arguments
            if (argument is "-c")
            {
                i++;
                continue;
            }

            if (argument.StartsWith('-'))
            {
                continue;
            }

            return argument;
        }

        return arguments.Length is 0 ? string.Empty : arguments[0];
    }

    private async Task<SettingsGitRepositoryState> GetRepositoryStateCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(RepositoryPath);

            SettingsGitCommandResult version = await RunGitAsync(["--version"], null, cancellationToken);
            if (version.GitUnavailable)
            {
                return new()
                {
                    FailureKind = SettingsGitFailureKind.GitUnavailable,
                    Detail = version.NormalizedCombinedOutput,
                };
            }

            if (!version.Succeeded)
            {
                return new()
                {
                    FailureKind = version.FailureKind,
                    Detail = version.NormalizedCombinedOutput,
                };
            }

            SettingsGitCommandResult topLevel = await RunGitAsync(["rev-parse", "--show-toplevel"], RepositoryPath, cancellationToken);
            if (!topLevel.Succeeded)
            {
                string dotGit = Path.Combine(RepositoryPath, ".git");
                SettingsGitFailureKind failureKind = (Directory.Exists(dotGit) || File.Exists(dotGit)) ? topLevel.FailureKind : SettingsGitFailureKind.None;

                return new()
                {
                    IsGitAvailable = true,
                    GitVersion = version.NormalizedOutput,
                    FailureKind = failureKind,
                    Detail = topLevel.NormalizedCombinedOutput,
                };
            }

            if (!Path.IsEqual(Path.GetFullPath(RepositoryPath), Path.GetFullPath(topLevel.Output.Trim())))
            {
                return new()
                {
                    IsGitAvailable = true,
                    GitVersion = version.NormalizedOutput,
                };
            }

            SettingsGitCommandResult remote = await RunGitAsync(["remote", "get-url", RemoteName], RepositoryPath, cancellationToken);
            return new()
            {
                IsGitAvailable = true,
                IsRepository = true,
                GitVersion = version.NormalizedOutput,
                RemoteUrl = remote.Succeeded ? remote.NormalizedOutput : null,
                FailureKind = remote.Succeeded ? SettingsGitFailureKind.None : SettingsGitFailureKind.Remote,
                Detail = remote.NormalizedCombinedOutput,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new()
            {
                FailureKind = SettingsGitFailureKind.Permission,
                Detail = ex.Message,
            };
        }
    }

    private async Task<SettingsGitOperationResult> InitializeRepositoryCoreAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.Remote);
        }

        try
        {
            Directory.CreateDirectory(RepositoryPath);

            SettingsGitRepositoryState state = await GetRepositoryStateAsync(cancellationToken);
            if (!state.IsGitAvailable)
            {
                return SettingsGitOperationResult.Failure(SettingsGitFailureKind.GitUnavailable, state.Detail);
            }

            if (!state.IsRepository)
            {
                if (await RunGitAsync(["init"], RepositoryPath, cancellationToken) is { Succeeded: false } init)
                {
                    return SettingsGitOperationResult.CommandFailure(init);
                }

                if (await RunGitAsync(["symbolic-ref", "HEAD", $"refs/heads/{DefaultBranchName}"], RepositoryPath, cancellationToken) is { Succeeded: false } setDefaultBranch)
                {
                    return SettingsGitOperationResult.CommandFailure(setDefaultBranch);
                }
            }

            SettingsGitCommandResult existingRemote = await RunGitAsync(["remote", "get-url", RemoteName], RepositoryPath, cancellationToken);
            SettingsGitCommandResult configureRemote = existingRemote.Succeeded
                ? await RunGitAsync(["remote", "set-url", RemoteName, remoteUrl.Trim()], RepositoryPath, cancellationToken)
                : await RunGitAsync(["remote", "add", RemoteName, remoteUrl.Trim()], RepositoryPath, cancellationToken);

            return SettingsGitOperationResult.Command(configureRemote);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.Permission, ex.Message);
        }
    }

    private async Task<SettingsGitOperationResult> PushCoreAsync(CancellationToken cancellationToken)
    {
        if (await EnsureRepositoryReadyAsync(cancellationToken) is { Succeeded: false } ready)
        {
            return ready;
        }

        if (EnsureNoRepositoryOperationInProgress() is { Succeeded: false } repositoryOperation)
        {
            return repositoryOperation;
        }

        if (await CommitSettingsChangesAsync("Sync settings", cancellationToken) is { Succeeded: false} commit)
        {
            return commit;
        }

        if (await RunGitAsync(["rev-parse", "--verify", "HEAD"], RepositoryPath, cancellationToken) is { Succeeded: false})
        {
            return SettingsGitOperationResult.Success(SettingsGitOperationDetailKind.NoLocalCommits);
        }

        if (await PushNormallyAsync(cancellationToken) is { Succeeded: true } push)
        {
            return SettingsGitOperationResult.Command(push);
        }

        return SettingsGitOperationResult.Command(await ForcePushAsync(cancellationToken));
    }

    private async Task<SettingsGitOperationResult> EnsureRepositoryReadyAsync(CancellationToken cancellationToken)
    {
        SettingsGitRepositoryState state = await GetRepositoryStateAsync(cancellationToken);
        if (!state.IsGitAvailable)
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.GitUnavailable, state.Detail);
        }

        if (!state.IsRepository)
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.Repository, state.Detail);
        }

        if (string.IsNullOrWhiteSpace(state.RemoteUrl))
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.Remote, state.Detail);
        }

        return SettingsGitOperationResult.Success();
    }

    private SettingsGitOperationResult EnsureNoRepositoryOperationInProgress()
    {
        if (!TryGetGitDirectory(out string gitDirectory))
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.Repository);
        }

        if (BlockingFiles.Any(fileName => File.Exists(Path.Combine(gitDirectory, fileName))) ||
            Directory.Exists(Path.Combine(gitDirectory, "rebase-apply")) ||
            Directory.Exists(Path.Combine(gitDirectory, "rebase-merge")))
        {
            return SettingsGitOperationResult.Failure(SettingsGitFailureKind.Repository, SettingsGitOperationDetailKind.RepositoryOperationInProgress);
        }

        return SettingsGitOperationResult.Success();
    }

    private async Task<SettingsGitOperationResult> CommitSettingsChangesAsync(string message, CancellationToken cancellationToken)
    {
        if (await RunGitAsync(["add", "--all", "--force"], RepositoryPath, cancellationToken) is { Succeeded: false} add)
        {
            return SettingsGitOperationResult.CommandFailure(add);
        }

        SettingsGitCommandResult diff = await RunGitAsync(["diff", "--cached", "--quiet"], RepositoryPath, cancellationToken);
        if (diff.ExitCode is 0)
        {
            return SettingsGitOperationResult.Success(SettingsGitOperationDetailKind.NoChanges);
        }

        if (diff.ExitCode is not 1)
        {
            return SettingsGitOperationResult.CommandFailure(diff);
        }

        SettingsGitCommandResult commit = await RunGitAsync(["-c", $"user.name={CommitAuthorName}", "-c", $"user.email={CommitAuthorEmail}", "commit", "-m", message], RepositoryPath, cancellationToken);
        return SettingsGitOperationResult.Command(commit, SettingsGitOperationDetailKind.Committed);
    }

    private async Task<SettingsGitCommandResult> PullFromDefaultRemoteBranchAsync(CancellationToken cancellationToken)
    {
        string remoteBranch = await GetDefaultRemoteBranchAsync(cancellationToken);
        SettingsGitCommandResult pull = await RunGitAsync(["pull", "--autostash", RemoteName, remoteBranch], RepositoryPath, cancellationToken);
        if (!pull.Succeeded)
        {
            return pull;
        }

        SettingsGitCommandResult currentBranch = await RunGitAsync(["branch", "--show-current"], RepositoryPath, cancellationToken);
        if (currentBranch.Succeeded && string.Equals(currentBranch.Output.Trim(), remoteBranch, StringComparison.Ordinal))
        {
            _ = await RunGitAsync(["branch", "--set-upstream-to", $"{RemoteName}/{remoteBranch}"], RepositoryPath, cancellationToken);
        }

        return pull;
    }

    private async Task<SettingsGitCommandResult> PullWithMergeAsync(CancellationToken cancellationToken)
    {
        return (await RunGitAsync(GetUpstreamArguments, RepositoryPath, cancellationToken)).Succeeded
            ? await RunGitAsync(["pull", "--autostash"], RepositoryPath, cancellationToken)
            : await PullFromDefaultRemoteBranchAsync(cancellationToken);
    }

    private async Task<SettingsGitCommandResult> PushNormallyAsync(CancellationToken cancellationToken)
    {
        SettingsGitCommandResult upstream = await RunGitAsync(GetUpstreamArguments, RepositoryPath, cancellationToken);
        return upstream.Succeeded
            ? await RunGitAsync(["push"], RepositoryPath, cancellationToken)
            : await RunGitAsync(["push", "-u", RemoteName, "HEAD"], RepositoryPath, cancellationToken);
    }

    private async Task<SettingsGitCommandResult> ForcePushAsync(CancellationToken cancellationToken)
    {
        SettingsGitCommandResult upstream = await RunGitAsync(GetUpstreamArguments, RepositoryPath, cancellationToken);
        if (upstream.Succeeded && !string.IsNullOrWhiteSpace(upstream.Output))
        {
            ReadOnlySpan<char> remoteBranch = GetBranchNameFromRemoteReference(upstream.Output.AsSpan().Trim());
            return await RunGitAsync(["push", "--force", RemoteName, $"HEAD:{remoteBranch}"], RepositoryPath, cancellationToken);
        }

        return await RunGitAsync(["push", "--force", "-u", RemoteName, "HEAD"], RepositoryPath, cancellationToken);
    }

    private async Task<SettingsGitOperationResult> ForcePullAsync(CancellationToken cancellationToken)
    {
        string remoteReference = await GetPullRemoteReferenceAsync(cancellationToken);
        if (await RunGitAsync(["fetch", "--prune", RemoteName], RepositoryPath, cancellationToken) is { Succeeded: false } fetch)
        {
            return SettingsGitOperationResult.CommandFailure(fetch);
        }

        if (await RunGitAsync(["reset", "--hard", remoteReference], RepositoryPath, cancellationToken) is { Succeeded: false } reset)
        {
            return SettingsGitOperationResult.CommandFailure(reset);
        }

        if (await RunGitAsync(["clean", "-d", "--force", "-x"], RepositoryPath, cancellationToken) is { Succeeded: false } clean)
        {
            return SettingsGitOperationResult.CommandFailure(clean);
        }

        SettingsGitCommandResult currentBranch = await RunGitAsync(["branch", "--show-current"], RepositoryPath, cancellationToken);

        ReadOnlySpan<char> remoteBranch = GetBranchNameFromRemoteReference(remoteReference);
        if (currentBranch.Succeeded && currentBranch.Output.AsSpan().Trim().Equals(remoteBranch, StringComparison.Ordinal))
        {
            _ = await RunGitAsync(["branch", "--set-upstream-to", remoteReference], RepositoryPath, cancellationToken);
        }

        return SettingsGitOperationResult.Success();
    }

    private async Task<string> GetPullRemoteReferenceAsync(CancellationToken cancellationToken)
    {
        SettingsGitCommandResult upstream = await RunGitAsync(GetUpstreamArguments, RepositoryPath, cancellationToken);
        if (upstream.Succeeded && !string.IsNullOrWhiteSpace(upstream.Output))
        {
            return upstream.Output.Trim();
        }

        string remoteBranch = await GetDefaultRemoteBranchAsync(cancellationToken);
        return $"{RemoteName}/{remoteBranch}";
    }

    private async Task<string> GetDefaultRemoteBranchAsync(CancellationToken cancellationToken)
    {
        SettingsGitCommandResult remoteHead = await RunGitAsync(["ls-remote", "--symref", RemoteName, "HEAD"], RepositoryPath, cancellationToken);
        if (remoteHead.Succeeded)
        {
            foreach (string line in remoteHead.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                const string Prefix = "ref: refs/heads/";
                if (line.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    int branchNameEnd = line.IndexOf('\t', Prefix.Length);
                    return branchNameEnd >= 0 ? line[Prefix.Length..branchNameEnd] : line[Prefix.Length..];
                }
            }
        }

        SettingsGitCommandResult currentBranch = await RunGitAsync(["branch", "--show-current"], RepositoryPath, cancellationToken);
        string branchName = currentBranch.Output.Trim();
        return string.IsNullOrWhiteSpace(branchName) ? DefaultBranchName : branchName;
    }

    private async Task<SettingsGitCommandResult> RunGitAsync(string[] arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        string commandName = GetGitCommandName(arguments);
        using SentryDiagnosticSpan span = SentryDiagnostics.StartSpan(SentryOperations.SettingsGitCommand, $"git {commandName}");
        span.SetTag(SentryTags.SettingsGitCommand, commandName);

        ProcessStartInfo startInfo = new()
        {
            FileName = GitFileName,
            WorkingDirectory = workingDirectory ?? RepositoryPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using (Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git."))
            {
#pragma warning disable CA2016
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
#pragma warning restore CA2016

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(true);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (Win32Exception)
                    {
                    }

                    await process.WaitForExitAsync(CancellationToken.None);
                    try
                    {
                        _ = await Task.WhenAll(outputTask, errorTask);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
                    {
                    }

                    throw;
                }

                SettingsGitCommandResult result = new()
                {
                    ExitCode = process.ExitCode,
                    Output = await outputTask,
                    Error = await errorTask,
                };

                return CompleteCommandSpan(span, result);
            }
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Win32Exception ex)
        {
            SettingsGitCommandResult result = new()
            {
                ExitCode = -1,
                Output = string.Empty,
                Error = ex.Message,
                GitUnavailable = true,
            };

            return CompleteCommandSpan(span, result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SettingsGitCommandResult result = new()
            {
                ExitCode = -1,
                Output = string.Empty,
                Error = ex.Message,
            };

            return CompleteCommandSpan(span, result);
        }
    }

    private bool TryGetGitDirectory(out string gitDirectory)
    {
        string dotGit = Path.Combine(RepositoryPath, ".git");
        if (Directory.Exists(dotGit))
        {
            gitDirectory = dotGit;
            return true;
        }

        if (!File.Exists(dotGit))
        {
            gitDirectory = string.Empty;
            return false;
        }

        try
        {
            string content = File.ReadAllText(dotGit).Trim();
            const string Prefix = "gitdir:";
            if (!content.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                gitDirectory = string.Empty;
                return false;
            }

            string path = content[Prefix.Length..].Trim();
            gitDirectory = Path.GetFullPath(path, RepositoryPath);
            return Directory.Exists(gitDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            gitDirectory = string.Empty;
            return false;
        }
    }
}
