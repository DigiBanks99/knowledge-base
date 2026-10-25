using System.ComponentModel;
using System.Diagnostics;

namespace KnowledgeBase.Agent.Wiki.Versioning;

/// <summary>Runs the <c>git</c> CLI as a child process, one command at a time.</summary>
internal sealed class GitClient : IGitClient
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    // Agents write inside the wiki directory, so nothing in the repository may run a command: hooks and the
    // filesystem monitor are switched off, and diffs never use an external tool.
    private static readonly string NoHooks = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    public bool HasRepository(string directory) => Path.Exists(Path.Combine(directory, ".git"));

    public async Task<Result<bool, Error>> InitAsync(string directory, CancellationToken cancellationToken) =>
        await RunAsync(directory, ["init", "--quiet", "--initial-branch=main"], cancellationToken)
            .Bind(Succeeded);

    public async Task<Result<bool, Error>> SetConfigAsync(
        string directory,
        string key,
        string value,
        CancellationToken cancellationToken) =>
        await RunAsync(directory, ["config", key, value], cancellationToken)
            .Bind(Succeeded);

    public async Task<Result<bool, Error>> StageAllAsync(string directory, CancellationToken cancellationToken) =>
        await RunAsync(directory, ["add", "--all"], cancellationToken)
            .Bind(Succeeded);

    public async Task<Result<bool, Error>> HasStagedChangesAsync(string directory, CancellationToken cancellationToken) =>
        await RunAsync(directory, ["diff", "--cached", "--quiet", "--no-ext-diff"], cancellationToken)
            .Bind(output => output.ExitCode switch
            {
                0 => Result.Success<bool, Error>(false),
                1 => Result.Success<bool, Error>(true),
                _ => Failed(output)
            });

    public async Task<Result<bool, Error>> CommitAsync(
        string directory,
        string message,
        string author,
        CancellationToken cancellationToken) =>
        await RunAsync(directory, ["commit", "--quiet", $"--author={author}", "--message", message], cancellationToken)
            .Bind(Succeeded);

    private static async Task<Result<GitOutput, Error>> RunAsync(
        string directory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        string command = string.Join(' ', arguments.TakeWhile(argument => !argument.StartsWith('-')));
        ProcessStartInfo startInfo = new("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // The wiki on the deployment VM is a bind mount owned by the host user, not the container's user, so git
        // would refuse it as having dubious ownership. Trusting it per command avoids depending on global config;
        // git compares the absolute path.
        string fullDirectory = Path.GetFullPath(directory);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(fullDirectory);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"safe.directory={fullDirectory.Replace('\\', '/')}");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"core.hooksPath={NoHooks}");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.fsmonitor=false");
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new GitCommandFailedError(command, $"git could not be started ({ex.Message}). Is it installed?");
        }

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new GitOutput(command, process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new GitCommandFailedError(command, $"it did not finish within {CommandTimeout.TotalSeconds} seconds.");
        }
    }

    private static Result<bool, Error> Succeeded(GitOutput output) =>
        output.ExitCode == 0 ? true : Failed(output);

    private static Result<bool, Error> Failed(GitOutput output) => new GitCommandFailedError(
        output.Command,
        $"exit code {output.ExitCode}. {output.StandardError.Trim()}".Trim());

    private sealed record GitOutput(string Command, int ExitCode, string StandardOutput, string StandardError);
}