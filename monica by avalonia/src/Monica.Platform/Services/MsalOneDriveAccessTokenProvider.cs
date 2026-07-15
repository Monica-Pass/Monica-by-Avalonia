using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Monica.Data;

namespace Monica.Platform.Services;

public interface IOneDriveAccessTokenProvider
{
    Task<OneDriveAccountInfo?> GetCachedAccountAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default);
    Task<OneDriveSignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default);
    Task<string> GetAccessTokenAsync(
        string accountId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
    Task SignOutAsync(string? accountId = null, CancellationToken cancellationToken = default);
    Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default);
}

public sealed class OneDriveAccountUnavailableException(string message) : InvalidOperationException(message);

public sealed class MsalOneDriveAccessTokenProvider : IOneDriveAccessTokenProvider
{
    public const string ClientId = "2aaf8c2c-b817-4085-9517-586a4a113dfc";
    private const string Authority = "https://login.microsoftonline.com/common";
    private const string CacheFileName = "onedrive-msal-cache.bin";
    private const string MacKeychainService = "com.monicapass.desktop.onedrive";
    private const string MacKeychainAccount = "msal-cache";
    private const string LinuxKeyringSchema = "com.monicapass.desktop.onedrive";
    private const string LinuxKeyringLabel = "Monica OneDrive token cache";
    private static readonly string[] Scopes = ["User.Read", "Files.ReadWrite"];

    private readonly IPublicClientApplication _application;
    private readonly Task _cacheInitialization;
    private string? _selectedAccountId;
    private MsalCacheHelper? _cacheHelper;

    public MsalOneDriveAccessTokenProvider()
    {
        _application = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
            .WithLogging(
                (_, _, _) => { },
                LogLevel.Warning,
                enablePiiLogging: false,
                enableDefaultPlatformLogging: false)
            .Build();
        _cacheInitialization = TryRegisterSecureCacheAsync();
    }

    public async Task<OneDriveAccountInfo?> GetCachedAccountAsync(
        string? accountId = null,
        CancellationToken cancellationToken = default)
    {
        await _cacheInitialization.WaitAsync(cancellationToken);
        var accounts = (await _application.GetAccountsAsync()).ToArray();
        var resolvedId = string.IsNullOrWhiteSpace(accountId) ? _selectedAccountId : accountId;
        var account = string.IsNullOrWhiteSpace(resolvedId)
            ? accounts.Length == 1 ? accounts[0] : null
            : accounts.FirstOrDefault(candidate => string.Equals(GetAccountId(candidate), resolvedId, StringComparison.Ordinal));
        return account is null ? null : ToAccountInfo(account);
    }

    public async Task<OneDriveSignInChallenge> BeginSignInAsync(CancellationToken cancellationToken = default)
    {
        await _cacheInitialization.WaitAsync(cancellationToken);
        var promptSource = new TaskCompletionSource<OneDriveDeviceCodePrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
        var authentication = _application
            .AcquireTokenWithDeviceCode(Scopes, result =>
            {
                promptSource.TrySetResult(new OneDriveDeviceCodePrompt(
                    result.UserCode,
                    new Uri(result.VerificationUrl, UriKind.Absolute),
                    result.ExpiresOn,
                    result.Message));
                return Task.CompletedTask;
            })
            .ExecuteAsync(cancellationToken);
        var completion = CompleteSignInAsync(authentication);
        _ = completion.ContinueWith(
            task =>
            {
                if (task.IsCanceled)
                {
                    promptSource.TrySetCanceled(cancellationToken);
                }
                else if (task.Exception is { } error)
                {
                    promptSource.TrySetException(error.InnerExceptions);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var prompt = await promptSource.Task.WaitAsync(cancellationToken);
        return new OneDriveSignInChallenge(prompt, completion);
    }

    public async Task<string> GetAccessTokenAsync(
        string accountId,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await _cacheInitialization.WaitAsync(cancellationToken);
        var account = (await _application.GetAccountsAsync())
            .FirstOrDefault(candidate => string.Equals(GetAccountId(candidate), accountId, StringComparison.Ordinal))
            ?? throw new OneDriveAccountUnavailableException("The Microsoft account linked to this OneDrive source is no longer signed in.");
        try
        {
            var result = await _application
                .AcquireTokenSilent(Scopes, account)
                .WithForceRefresh(forceRefresh)
                .ExecuteAsync(cancellationToken);
            _selectedAccountId = GetAccountId(result.Account);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException ex)
        {
            throw new OneDriveAccountUnavailableException(
                $"The Microsoft account linked to this OneDrive source requires sign-in: {ex.ErrorCode}");
        }
    }

    public async Task SignOutAsync(string? accountId = null, CancellationToken cancellationToken = default)
    {
        await _cacheInitialization.WaitAsync(cancellationToken);
        var accounts = (await _application.GetAccountsAsync()).ToArray();
        foreach (var account in accounts.Where(candidate =>
                     string.IsNullOrWhiteSpace(accountId) ||
                     string.Equals(GetAccountId(candidate), accountId, StringComparison.Ordinal)))
        {
            await _application.RemoveAsync(account);
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.Equals(_selectedAccountId, accountId, StringComparison.Ordinal))
        {
            _selectedAccountId = null;
        }
    }

    public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private async Task<OneDriveAccountInfo> CompleteSignInAsync(Task<AuthenticationResult> authentication)
    {
        var result = await authentication;
        _selectedAccountId = GetAccountId(result.Account);
        var displayName = result.ClaimsPrincipal?.FindFirst("name")?.Value;
        return new OneDriveAccountInfo(
            _selectedAccountId,
            string.IsNullOrWhiteSpace(displayName) ? result.Account.Username : displayName,
            result.Account.Username);
    }

    private async Task TryRegisterSecureCacheAsync()
    {
        try
        {
            var cacheDirectory = MonicaAppDataPaths.GetPath("identity");
            Directory.CreateDirectory(cacheDirectory);
            var properties = new StorageCreationPropertiesBuilder(CacheFileName, cacheDirectory)
                .WithMacKeyChain(MacKeychainService, MacKeychainAccount)
                .WithLinuxKeyring(
                    LinuxKeyringSchema,
                    MsalCacheHelper.LinuxKeyRingDefaultCollection,
                    LinuxKeyringLabel,
                    new KeyValuePair<string, string>("Version", "1"),
                    new KeyValuePair<string, string>("ProductGroup", "Monica"))
                .Build();
            var helper = await MsalCacheHelper.CreateAsync(properties);
            helper.VerifyPersistence();
            helper.RegisterCache(_application.UserTokenCache);
            _cacheHelper = helper;
        }
        catch (Exception ex) when (ex is MsalCachePersistenceException or
                                   PlatformNotSupportedException or
                                   DllNotFoundException or
                                   InvalidOperationException)
        {
            // Continue with MSAL's in-memory cache. Never opt into an unprotected token-cache file.
            _cacheHelper = null;
        }
    }

    private static OneDriveAccountInfo ToAccountInfo(IAccount account) =>
        new(GetAccountId(account), account.Username, account.Username);

    private static string GetAccountId(IAccount account) =>
        account.HomeAccountId?.Identifier?.TakeIfNotBlank() ??
        throw new OneDriveAccountUnavailableException("Microsoft identity did not return a stable home-account identifier.");
}

internal static class OneDriveIdentityStringExtensions
{
    internal static string? TakeIfNotBlank(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
