using System.Security.Cryptography;
using System.Text;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Core.Services;
using Monica.Data.Passkeys;

namespace Monica.Tests;

public sealed class PasskeyServiceBoundaryTests
{
    [Theory]
    [InlineData("https://evil.test", 32, 4)]
    [InlineData("https://example.com.evil.test", 32, 4)]
    [InlineData("https://example.com", 15, 4)]
    [InlineData("https://example.com", 32, 0)]
    [InlineData("https://example.com", 32, 65)]
    public async Task InvalidSoftwareRegistrationIsRefusedBeforeAnyPersistence(
        string origin, int challengeLength, int userHandleLength)
    {
        var store = new CountingStore();
        var service = new PasskeyService(store);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new PasskeyCreateRequest(
            "example.com", new byte[challengeLength], new byte[userHandleLength], "alice", Origin: origin)));

        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, store.PrivateKeyResolutionCount);
    }

    [Theory]
    [InlineData(false, "https://evil.test", 32)]
    [InlineData(false, "https://example.com", 15)]
    [InlineData(true, "https://evil.test", 32)]
    [InlineData(true, "https://example.com", 15)]
    public async Task InvalidAssertionIsRefusedBeforeAccessingEitherAuthenticator(
        bool useNative, string origin, int challengeLength)
    {
        var store = new CountingStore { Entry = MakeEntry() };
        var native = new FakeNativeAuthenticator();
        if (useNative) store.Entry.PasskeyMode = PasskeyModes.WindowsHello;
        var service = new PasskeyService(store, native);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AssertAsync(
            store.Entry.CredentialId, new byte[challengeLength], "example.com", origin));

        Assert.Equal(0, store.PrivateKeyResolutionCount);
        Assert.Equal(0, native.AssertionCount);
        Assert.Equal(0, store.MarkUsedCount);
    }

    [Fact]
    public async Task RequiredDiscoverabilityMustBeConfirmedByThePlatform()
    {
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { IsDiscoverable = false };
        var service = new PasskeyService(store, native);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlatformRegistrationAsync(
            ValidRequest(), PasskeyModes.WindowsHello));

        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task PlatformMetadataUsesTheActualDiscoverabilityOfTheCreatedCredential()
    {
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { IsDiscoverable = true };
        var service = new PasskeyService(store, native);

        var result = await service.CreatePlatformRegistrationAsync(
            ValidRequest() with { IsDiscoverable = false }, PasskeyModes.WindowsHello);

        Assert.True(result.Entry.IsDiscoverable);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task NativeAssertionsMustBelongToTheSelectedAccount()
    {
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { ReturnedUserHandle = Encoding.UTF8.GetBytes("other-account") };
        var service = new PasskeyService(store, native);
        var result = await service.CreatePlatformRegistrationAsync(ValidRequest(), PasskeyModes.WindowsHello);
        native.Entry = result.Entry;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssertAsync(
            result.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com"));

        Assert.Contains("different account", failure.Message);
        Assert.Equal(0, store.MarkUsedCount);
    }

    [Fact]
    public async Task AnAllowedCredentialAssertionMayOmitItsUserHandle()
    {
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { ReturnedUserHandle = [] };
        var service = new PasskeyService(store, native);
        var result = await service.CreatePlatformRegistrationAsync(ValidRequest(), PasskeyModes.WindowsHello);
        native.Entry = result.Entry;

        var assertion = await service.AssertAsync(
            result.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com");

        Assert.NotNull(assertion);
        Assert.Equal(1, store.MarkUsedCount);
    }

    [Fact]
    public async Task CancellationAfterTheNativeCeremonyDoesNotMarkACredentialUsed()
    {
        var store = new CountingStore();
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNativeAuthenticator { AfterAssertion = cancellation.Cancel };
        var service = new PasskeyService(store, native);
        var result = await service.CreatePlatformRegistrationAsync(ValidRequest(), PasskeyModes.WindowsHello);
        native.Entry = result.Entry;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssertAsync(
            result.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com",
            cancellationToken: cancellation.Token));

        Assert.Equal(0, store.MarkUsedCount);
    }

    [Fact]
    public async Task CancellationAfterNativeRegistrationDoesNotPersistACredential()
    {
        var store = new CountingStore();
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNativeAuthenticator { AfterCreate = cancellation.Cancel };
        var service = new PasskeyService(store, native);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreatePlatformRegistrationAsync(
            ValidRequest(), PasskeyModes.WindowsHello, cancellationToken: cancellation.Token));

        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegistrationAttestationMustBeValidAndBindTheReturnedAuthenticatorData(bool malformed)
    {
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator
        {
            TamperAttestation = true,
            ReturnMalformedAttestation = malformed
        };
        var service = new PasskeyService(store, native);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePlatformRegistrationAsync(
            ValidRequest(), PasskeyModes.WindowsHello));

        Assert.Contains("attestation", error.Message);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task ASignedAssertionMustNotIncludeRegistrationOnlyCredentialData()
    {
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { UseRegistrationAuthDataForAssertion = true };
        var service = new PasskeyService(store, native);
        var result = await service.CreatePlatformRegistrationAsync(ValidRequest(), PasskeyModes.WindowsHello);
        native.Entry = result.Entry;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssertAsync(
            result.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com"));

        Assert.Equal(0, store.MarkUsedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALockedVaultCannotCreateSoftwareOrNativeCredentials(bool useNative)
    {
        using var session = new VaultSessionService();
        session.MarkLocked();
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator();
        var service = new PasskeyService(store, native, session);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (useNative)
                await service.CreatePlatformRegistrationAsync(ValidRequest(), PasskeyModes.WindowsHello);
            else
                await service.CreateAsync(ValidRequest());
        });

        Assert.Equal(0, native.CreateCount);
        Assert.Equal(0, store.SaveCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALockedVaultCannotUseSoftwareOrNativeCredentials(bool useNative)
    {
        using var session = new VaultSessionService();
        session.MarkLocked();
        var store = new CountingStore { Entry = MakeEntry() };
        if (useNative) store.Entry.PasskeyMode = PasskeyModes.WindowsHello;
        var native = new FakeNativeAuthenticator();
        var service = new PasskeyService(store, native, session);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssertAsync(
            store.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com"));

        Assert.Equal(0, store.FindCount);
        Assert.Equal(0, native.AssertionCount);
        Assert.Equal(0, store.PrivateKeyResolutionCount);
        Assert.Equal(0, store.MarkUsedCount);
    }

    [Fact]
    public async Task LockingAndImmediatelyReopeningTheVaultDuringNativeRegistrationCancelsTheOldOperation()
    {
        using var session = new VaultSessionService();
        session.MarkUnlocked();
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { AfterCreate = () => LockAndReopen(session) };
        var service = new PasskeyService(store, native, session);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreatePlatformRegistrationAsync(
            ValidRequest(), PasskeyModes.WindowsHello));

        Assert.True(session.IsUnlocked);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task LockingAndImmediatelyReopeningTheVaultDuringNativeAssertionCancelsTheOldOperation()
    {
        using var session = new VaultSessionService();
        session.MarkUnlocked();
        var store = new CountingStore();
        var native = new FakeNativeAuthenticator { AfterAssertion = () => LockAndReopen(session) };
        var service = new PasskeyService(store, native, session);
        var result = await service.CreatePlatformRegistrationAsync(ValidRequest(), PasskeyModes.WindowsHello);
        native.Entry = result.Entry;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssertAsync(
            result.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com"));

        Assert.True(session.IsUnlocked);
        Assert.Equal(0, store.MarkUsedCount);
    }

    [Fact]
    public async Task ASessionChangedDuringCredentialLookupCannotContinueToReadAPrivateKey()
    {
        using var session = new VaultSessionService();
        session.MarkUnlocked();
        var store = new CountingStore { Entry = MakeEntry(), AfterFind = () => LockAndReopen(session) };
        var service = new PasskeyService(store, vaultSession: session);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssertAsync(
            store.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com"));

        Assert.Equal(0, store.PrivateKeyResolutionCount);
        Assert.Equal(0, store.MarkUsedCount);
    }

    [Fact]
    public async Task ASessionChangedDuringPrivateKeyResolutionCannotContinueToSign()
    {
        using var session = new VaultSessionService();
        session.MarkUnlocked();
        var store = new CountingStore
        {
            Entry = MakeEntry(),
            AfterPrivateKeyResolution = () => LockAndReopen(session)
        };
        var service = new PasskeyService(store, vaultSession: session);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AssertAsync(
            store.Entry.CredentialId, RandomNumberGenerator.GetBytes(32), "example.com"));

        Assert.Equal(1, store.PrivateKeyResolutionCount);
        Assert.Equal(0, store.MarkUsedCount);
    }

    private static void LockAndReopen(VaultSessionService session)
    {
        session.MarkLocked();
        session.MarkUnlocked();
    }

    private static PasskeyCreateRequest ValidRequest() => new(
        "example.com", RandomNumberGenerator.GetBytes(32), Encoding.UTF8.GetBytes("user"), "alice");

    private static PasskeyEntry MakeEntry() => new()
    {
        Id = 1,
        CredentialId = PasskeyBase64Url.Encode(PasskeyCredentialId.NewRandom()),
        RpId = "example.com",
        UserId = PasskeyBase64Url.Encode(Encoding.UTF8.GetBytes("user")),
        PasskeyMode = PasskeyModes.BitwardenCompatible
    };

    private sealed class CountingStore : IPasskeyStore
    {
        public PasskeyEntry? Entry { get; set; }
        public int SaveCount { get; private set; }
        public int MarkUsedCount { get; private set; }
        public int PrivateKeyResolutionCount { get; private set; }
        public int FindCount { get; private set; }
        public Action? AfterFind { get; set; }
        public Action? AfterPrivateKeyResolution { get; set; }

        public Task<long> SaveAsync(PasskeyEntry entry, string? privateKeyPkcs8Base64 = null,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            entry.Id = 1;
            Entry = entry;
            return Task.FromResult(entry.Id);
        }

        public Task<PasskeyEntry?> GetAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entry);

        public Task<PasskeyEntry?> FindAsync(string credentialId, string? rpId = null,
            CancellationToken cancellationToken = default)
        {
            FindCount++;
            AfterFind?.Invoke();
            return Task.FromResult(Entry is not null && (rpId is null || Entry.RpId == rpId) ? Entry : null);
        }

        public Task<IReadOnlyList<PasskeyEntry>> ListByRpIdAsync(string rpId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PasskeyEntry>>([]);

        public Task<string?> ResolvePrivateKeyAsync(PasskeyEntry entry,
            CancellationToken cancellationToken = default)
        {
            PrivateKeyResolutionCount++;
            AfterPrivateKeyResolution?.Invoke();
            return Task.FromResult<string?>(null);
        }

        public Task MarkUsedAsync(long id, CancellationToken cancellationToken = default)
        {
            MarkUsedCount++;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class FakeNativeAuthenticator : INativePasskeyAuthenticator
    {
        private PasskeyRegistration? _registration;
        public PasskeyEntry? Entry { get; set; }
        public bool IsDiscoverable { get; set; } = true;
        public byte[]? ReturnedUserHandle { get; set; }
        public Action? AfterCreate { get; set; }
        public Action? AfterAssertion { get; set; }
        public bool TamperAttestation { get; set; }
        public bool ReturnMalformedAttestation { get; set; }
        public bool UseRegistrationAuthDataForAssertion { get; set; }
        public int AssertionCount { get; private set; }
        public int CreateCount { get; private set; }
        public bool IsAvailable => true;
        public bool IsUserVerifyingPlatformAuthenticatorAvailable => true;

        public Task<NativePasskeyRegistration> CreateAsync(NativePasskeyCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            _registration = PasskeyAuthenticator.Register(request.RpId, request.Challenge, request.Algorithm, request.Origin);
            var attestation = _registration.AttestationObject;
            if (TamperAttestation)
            {
                attestation = ReturnMalformedAttestation
                    ? [0xff]
                    : PasskeyAuthenticator.BuildNoneAttestationObject(new byte[37]);
            }
            AfterCreate?.Invoke();
            return Task.FromResult(new NativePasskeyRegistration(
                _registration.CredentialId, _registration.AuthenticatorData, attestation,
                _registration.ClientData.Json, "internal", IsDiscoverable, true));
        }

        public Task<NativePasskeyAssertion> GetAssertionAsync(NativePasskeyAssertionRequest request,
            CancellationToken cancellationToken = default)
        {
            AssertionCount++;
            var registration = _registration ?? throw new InvalidOperationException("No credential registered.");
            var entry = Entry ?? throw new InvalidOperationException("No credential selected.");
            var assertion = PasskeyAuthenticator.Assert(entry, registration.KeyMaterial.PrivateKeyPkcs8Base64,
                request.Challenge, request.RpId, request.Origin);
            var authData = UseRegistrationAuthDataForAssertion ? registration.AuthenticatorData : assertion.AuthenticatorData;
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(registration.KeyMaterial.PrivateKeyPkcs8Base64), out _);
            var signature = key.SignData([.. authData, .. assertion.ClientData.ClientDataHash],
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            AfterAssertion?.Invoke();
            return Task.FromResult(new NativePasskeyAssertion(registration.CredentialId, authData,
                signature, ReturnedUserHandle ?? Encoding.UTF8.GetBytes("user"), assertion.ClientData.Json, "internal", true));
        }
    }
}
