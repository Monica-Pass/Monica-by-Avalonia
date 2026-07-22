using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Monica.Platform.Services;

public sealed class WindowsBrowserBridgeService(IPlatformIntegrationService platformIntegrationService) : IBrowserBridgeService
{
    private readonly object _sync = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellation;
    private Task _serverTask = Task.CompletedTask;
    private readonly SemaphoreSlim _clientGate = new(4, 4);
    private Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>>? _queryCredentials;

    public PlatformIntegrationCapability Capability =>
        platformIntegrationService.GetCapability(PlatformFeatureKeys.BrowserBridge);
    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public string SessionToken { get; private set; } = "";
    public string LastError { get; private set; } = "";

    public bool TryStart(
        int port,
        Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials)
    {
        ArgumentNullException.ThrowIfNull(queryCredentials);
        Stop();
        if (!OperatingSystem.IsWindows())
        {
            LastError = Capability.UnsupportedReason ?? "Browser integration requires Windows.";
            return false;
        }

        if (port is < 1024 or > 65535)
        {
            LastError = "Browser integration port must be between 1024 and 65535.";
            return false;
        }

        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start(16);
            var cancellation = new CancellationTokenSource();
            var token = CreateSessionToken();
            lock (_sync)
            {
                _listener = listener;
                _cancellation = cancellation;
                _queryCredentials = queryCredentials;
                SessionToken = token;
                Port = port;
                IsRunning = true;
                LastError = "";
                _serverTask = RunServerAsync(listener, token, queryCredentials, cancellation.Token);
            }
            return true;
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            LastError = exception.Message;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        TcpListener? listener;
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            listener = _listener;
            cancellation = _cancellation;
            _listener = null;
            _cancellation = null;
            _queryCredentials = null;
            IsRunning = false;
            Port = 0;
            SessionToken = "";
        }

        cancellation?.Cancel();
        listener?.Stop();
        cancellation?.Dispose();
    }

    public void Dispose() => Stop();

    private async Task RunServerAsync(
        TcpListener listener,
        string token,
        Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                try
                {
                    await _clientGate.WaitAsync(cancellationToken);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }

                _ = ProcessClientAsync(client, token, queryCredentials, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException exception) when (cancellationToken.IsCancellationRequested)
        {
            _ = exception;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Stop();
        }
    }

    private async Task ProcessClientAsync(
        TcpClient client,
        string token,
        Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials,
        CancellationToken cancellationToken)
    {
        try
        {
            await BrowserBridgeProtocol.HandleClientAsync(client, token, queryCredentials, cancellationToken);
        }
        finally
        {
            _clientGate.Release();
        }
    }

    private static string CreateSessionToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return token.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
