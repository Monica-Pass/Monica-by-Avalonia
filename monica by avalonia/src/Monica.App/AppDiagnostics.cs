using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Monica.Data;

namespace Monica.App;

internal static class AppDiagnostics
{
    private const int QueueCapacity = 4_096;

    // This log sits next to the vault, so it has to be bounded: the append-only writer reached
    // 215 MB on a development machine where one launch path threw on every start.
    internal const long MaxLogBytes = 2 * 1024 * 1024;

    // A rolled segment is kept as runtime.log.1, so the retained pair stays under these two numbers
    // together. Anything past the backup ceiling predates the cap and is dropped rather than rotated.
    internal const long MaxBackupBytes = 4 * 1024 * 1024;

    private static readonly string LogPath = MonicaAppDataPaths.GetPath("runtime.log");
    private static readonly Channel<DiagnosticEvent> LogEvents = Channel.CreateBounded<DiagnosticEvent>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    private static readonly Task WriterTask = Task.Run(ProcessLogQueueAsync);

    static AppDiagnostics()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushForShutdown();
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", message, exception);

    public static async Task<T> MeasureAsync<T>(string name, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Info($"{name} started");
            var result = await action();
            Info($"{name} completed in {stopwatch.ElapsedMilliseconds} ms");
            return result;
        }
        catch (Exception ex)
        {
            Error($"{name} failed after {stopwatch.ElapsedMilliseconds} ms", ex);
            throw;
        }
    }

    public static async Task MeasureAsync(string name, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Info($"{name} started");
            await action();
            Info($"{name} completed in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Error($"{name} failed after {stopwatch.ElapsedMilliseconds} ms", ex);
            throw;
        }
    }

    public static void Measure(string name, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Info($"{name} started");
            action();
            Info($"{name} completed in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Error($"{name} failed after {stopwatch.ElapsedMilliseconds} ms", ex);
            throw;
        }
    }

    private static void Write(string level, string message, Exception? exception = null)
    {
        if (!LogEvents.Writer.TryWrite(new DiagnosticEvent(DateTimeOffset.UtcNow, level, message, exception)))
        {
            Debug.WriteLine(message);
        }
    }

    private static async Task ProcessLogQueueAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            while (!await WriteLogSegmentAsync().ConfigureAwait(false))
            {
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Runtime diagnostic writer failed: {exception}");
            while (LogEvents.Reader.TryRead(out var diagnosticEvent))
            {
                Debug.WriteLine(Format(diagnosticEvent));
            }
        }
    }

    // True once the channel is drained for good, false when the size cap ended this segment early.
    private static async Task<bool> WriteLogSegmentAsync()
    {
        RollLogIfOverlong(LogPath, MaxLogBytes, MaxBackupBytes);
        await using var stream = new FileStream(
            LogPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4_096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(stream);

        while (await LogEvents.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            var batch = new StringBuilder();
            while (LogEvents.Reader.TryRead(out var diagnosticEvent))
            {
                batch.Append(Format(diagnosticEvent));
            }

            if (batch.Length > 0)
            {
                // This handle stays open for the life of the process, and an append stream only
                // seeks to the end once - so any line another process wrote in between gets
                // overwritten from the middle. Measured: a refused second launch and the instance
                // it handed off to produced one record spliced into another's sentence, and the
                // handoff evidence disappeared. Re-ask for the end, then write the batch in one
                // piece so the two writers cannot land inside each other's line.
                stream.Seek(0, SeekOrigin.End);
                await writer.WriteAsync(batch.ToString()).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
            if (stream.Length > MaxLogBytes)
            {
                return false;
            }
        }

        return true;
    }

    internal static void RollLogIfOverlong(string logPath, long maxBytes, long maxBackupBytes)
    {
        try
        {
            if (!File.Exists(logPath) || new FileInfo(logPath).Length <= maxBytes)
            {
                return;
            }

            var backupPath = logPath + ".1";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            // A rolled segment overshoots the cap by at most one flush and is worth keeping for
            // support. A log far past it predates the cap entirely, and its tail is not worth the
            // disk it would occupy - drop it and start clean.
            if (new FileInfo(logPath).Length > maxBackupBytes)
            {
                File.Delete(logPath);
                return;
            }

            File.Move(logPath, backupPath);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Runtime diagnostic log could not be rolled: {exception}");
        }
    }

    private static string Format(DiagnosticEvent diagnosticEvent)
    {
        var message = diagnosticEvent.Exception is null
            ? diagnosticEvent.Message
            : $"{diagnosticEvent.Message}: {diagnosticEvent.Exception}";
        return $"[{diagnosticEvent.Timestamp:O}] [{diagnosticEvent.Level}] {message}{Environment.NewLine}";
    }

    private static void FlushForShutdown()
    {
        LogEvents.Writer.TryComplete();
        try
        {
            WriterTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Runtime diagnostic flush failed: {exception}");
        }
    }

    private readonly record struct DiagnosticEvent(
        DateTimeOffset Timestamp,
        string Level,
        string Message,
        Exception? Exception);
}
