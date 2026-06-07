using System.Diagnostics;
using Monica.Data;

namespace Monica.App;

internal static class AppDiagnostics
{
    private static readonly object SyncRoot = new();
    private static readonly string LogPath = MonicaAppDataPaths.GetPath("runtime.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}: {exception}");

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

    private static void Write(string level, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"[{DateTimeOffset.Now:O}] [{level}] {message}{Environment.NewLine}";
            lock (SyncRoot)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            Debug.WriteLine(message);
        }
    }
}
