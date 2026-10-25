using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

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

    public async Task<Result<bool, Error>> SetRemoteAsync(
        string directory,
        string name,
        string url,
        CancellationToken cancellationToken) =>
        await RunAsync(directory, ["remote", "get-url", name], cancellationToken)
            .Bind(async output => output.ExitCode switch
            {
                0 when output.StandardOutput.Trim() == url => Result.Success<bool, Error>(false),
                0 => await RunAsync(directory, ["remote", "set-url", name, url], cancellationToken).Bind(Succeeded),
                // git answers 2 when there is no such remote.
                2 => await RunAsync(directory, ["remote", "add", name, url], cancellationToken).Bind(Succeeded),
                _ => Failed(output)
            });

    public async Task<Result<bool, Error>> PushAsync(
        string directory,
        string remote,
        string? sshCommand,
        CancellationToken cancellationToken) =>
        await RunAsync(
                directory,
                ["push", "--quiet", "--set-upstream", remote, "HEAD"],
                cancellationToken,
                sshCommand is null ? [] : [new KeyValuePair<string, string>("GIT_SSH_COMMAND", sshCommand)])
            .Bind(Succeeded);

    // Commits reachable from HEAD but from none of the remote's branches. Unlike @{u}..HEAD, this also answers before
    // the first push, when there is no upstream yet.
    public async Task<Result<int, Error>> CountUnpushedCommitsAsync(
        string directory,
        string remote,
        CancellationToken cancellationToken) =>
        await RunAsync(directory, ["rev-list", "--count", "HEAD", "--not", $"--remotes={remote}"], cancellationToken)
            .Bind(output => output.ExitCode == 0 && int.TryParse(output.StandardOutput.Trim(), out int count)
                ? Result.Success<int, Error>(count)
                : Result.Failure<int, Error>(Failed(output).Error));

    public async Task<Result<GitCommit?, Error>> GetLastCommitAsync(string directory, CancellationToken cancellationToken) =>
        await RunAsync(directory, ["log", "-1", "--format=%h%x00%cI%x00%s"], cancellationToken)
            .Bind(output =>
            {
                if (output.ExitCode != 0)
                {
                    // A branch without commits has nothing to log yet; that is not a failure.
                    return output.StandardError.Contains("does not have any commits", StringComparison.Ordinal)
                        ? Result.Success<GitCommit?, Error>(null)
                        : Result.Failure<GitCommit?, Error>(Failed(output).Error);
                }

                string[] fields = output.StandardOutput.TrimEnd('\n', '\r').Split('\0');
                return fields.Length == 3 && DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture, out DateTimeOffset committedAt)
                    ? Result.Success<GitCommit?, Error>(new GitCommit(fields[0], committedAt, fields[2]))
                    : Result.Failure<GitCommit?, Error>(new GitCommandFailedError(output.Command, $"its output could not be read: {output.StandardOutput}"));
            });

    private static async Task<Result<GitOutput, Error>> RunAsync(
        string directory,
        string[] arguments,
        CancellationToken cancellationToken,
        IEnumerable<KeyValuePair<string, string>>? environment = null)
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
        // Messages stay in English whatever the machine's locale, because some outcomes are recognised by their text.
        startInfo.Environment["LC_ALL"] = "C";
        foreach ((string name, string value) in environment ?? [])
        {
            startInfo.Environment[name] = value;
        }

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