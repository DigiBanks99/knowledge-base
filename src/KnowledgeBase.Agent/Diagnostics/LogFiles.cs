using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using KnowledgeBase.Agent.Wiki;

using Microsoft.Extensions.Options;

namespace KnowledgeBase.Agent.Diagnostics;

/// <summary>
/// Writes log entries as JSON lines, one file per UTC day in the logs directory, and deletes the files older than
/// <see cref="RetentionDays"/>. The files sit beside the wiki, not inside its git repository, so they are never
/// committed or pushed with it.
/// </summary>
/// <remarks>
/// This is built by the logging system itself, so it cannot take a logger: asking for one would be a circular
/// dependency. <see cref="RestoreInto"/> is given the logger it reports with.
/// Writing is best effort. A logging call must never fail the code that logged, so a write that fails is dropped and
/// retried with the next entry. Logging a failure would write another failure, so none is logged here.
/// </remarks>
internal sealed class LogFiles(
    IOptions<WikiConfigurationSettings> settings,
    TimeProvider timeProvider) : IDisposable
{
    public const int RetentionDays = 5;

    private const string FilePrefix = "api-";
    private const string FileSuffix = ".log";
    private const string DayFormat = "yyyy-MM-dd";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory = settings.Value.LogsDirectory;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _writerDay;

    /// <summary>Writes one entry. Never throws: a failed write leaves the entry in memory and on the console.</summary>
    public void Append(LogEntry entry)
    {
        lock (_gate)
        {
            try
            {
                DateOnly day = DateOnly.FromDateTime(entry.At.UtcDateTime);
                if (_writer is null || day != _writerDay)
                {
                    _writer?.Dispose();
                    _writer = null;
                    Directory.CreateDirectory(_directory);
                    _writer = new StreamWriter(PathFor(day), append: true, Utf8) { AutoFlush = true };
                    _writerDay = day;
                    Prune(day);
                }

                _writer.WriteLine(JsonSerializer.Serialize(entry, Json));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The writer is reopened on the next entry, which is when a disk that was full may have space again.
                _writer?.Dispose();
                _writer = null;
            }
        }
    }

    /// <summary>
    /// Loads up to <paramref name="limit"/> of the most recent entries on disk into <paramref name="buffer"/>, oldest
    /// first, so the Logs page has history straight after a restart. Lines that do not parse are skipped.
    /// </summary>
    public void RestoreInto(LogBuffer buffer, int limit, ILogger logger)
    {
        List<LogEntry> newestFirst = [];
        try
        {
            lock (_gate)
            {
                Prune(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
                foreach (string file in FilesNewestFirst())
                {
                    string[] lines = File.ReadAllLines(file, Utf8);
                    for (int index = lines.Length - 1; index >= 0 && newestFirst.Count < limit; index--)
                    {
                        if (TryParse(lines[index]) is { } entry)
                        {
                            newestFirst.Add(entry);
                        }
                    }

                    if (newestFirst.Count >= limit)
                    {
                        break;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Startup carries on with an empty history; the files are still there for the next start.
            LoggerMessages.FailedToRestoreLogs(logger, exception);
            return;
        }

        newestFirst.Reverse();
        buffer.Restore(newestFirst);
        LoggerMessages.RestoredLogs(logger, newestFirst.Count);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>Deletes the day files that are past retention. Called with the lock held.</summary>
    private void Prune(DateOnly today)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_directory, $"{FilePrefix}*{FileSuffix}"))
            {
                if (DayOf(file) is { } day && today.DayNumber - day.DayNumber > RetentionDays)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An old file that cannot be deleted is tried again on the next day.
        }
    }

    private IEnumerable<string> FilesNewestFirst() =>
        !Directory.Exists(_directory)
            ? []
            : Directory.EnumerateFiles(_directory, $"{FilePrefix}*{FileSuffix}")
                .Select(file => (File: file, Day: DayOf(file)))
                .Where(item => item.Day is not null)
                .OrderByDescending(item => item.Day)
                .Select(item => item.File);

    private string PathFor(DateOnly day) => Path.Combine(_directory, $"{FilePrefix}{day.ToString(DayFormat, CultureInfo.InvariantCulture)}{FileSuffix}");

    /// <summary>The day a log file is for, read from its name; null for any other file in the directory.</summary>
    private static DateOnly? DayOf(string file)
    {
        string name = Path.GetFileName(file);
        if (!name.StartsWith(FilePrefix, StringComparison.Ordinal)
            || !name.EndsWith(FileSuffix, StringComparison.Ordinal)
            || name.Length <= FilePrefix.Length + FileSuffix.Length)
        {
            return null;
        }

        string text = name[FilePrefix.Length..^FileSuffix.Length];
        return DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day)
            ? day
            : null;
    }

    private static LogEntry? TryParse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<LogEntry>(line, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
