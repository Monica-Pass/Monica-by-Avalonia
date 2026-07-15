using System.Diagnostics;
using System.Threading.Channels;
using Monica.Data;

namespace Monica.App;

internal static class AppDiagnostics
{
    private const int QueueCapacity = 4_096;
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
        if (!LogEvents.Writer.TryWrite(new DiagnosticEvent(DateTimeOffset.Now, level, message, exception)))
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
                while (LogEvents.Reader.TryRead(out var diagnosticEvent))
                {
                    await writer.WriteAsync(Format(diagnosticEvent)).ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
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
