namespace KnowledgeBase.Agent.Common;

/// <summary>
/// Retries a file operation that Windows refused because another handle has the file open: a reader, the program that
/// wrote it, or a virus scanner looking at a file just written. Those handles are short-lived, so waiting them out
/// briefly keeps the operation. A file or folder that is gone fails at once.
/// </summary>
/// <remarks>
/// The delays are real time rather than a <see cref="TimeProvider"/>'s, as they wait on other programs.
/// </remarks>
internal static class FileInUseRetry
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400)
    ];

    /// <summary>
    /// Whether the file may have been in use. Windows reports a file open elsewhere as a sharing violation, or as access
    /// denied when replacing or moving it. On Linux, .NET enforces its own file shares with advisory locks and reports
    /// a conflict with the platform's error number, so any I/O failure other than a missing file or folder counts.
    /// </summary>
    public static bool IsFileInUse(Exception exception) =>
        exception is UnauthorizedAccessException
        || exception is IOException and not (FileNotFoundException or DirectoryNotFoundException);

    public static async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (IsFileInUse(ex) && attempt < RetryDelays.Length)
            {
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }
    }

    public static Task RunAsync(Action operation, CancellationToken cancellationToken) =>
        RunAsync(
            () =>
            {
                operation();
                return Task.FromResult(true);
            },
            cancellationToken);
}
