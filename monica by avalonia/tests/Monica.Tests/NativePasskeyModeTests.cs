using System.Security.Cryptography;
using System.Text;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Data.Passkeys;

namespace Monica.Tests;

public sealed class NativePasskeyModeTests
{
    [Fact]
    public async Task Platform_mode_persists_public_material_and_uses_the_platform_for_assertions()
    {
        var challenge = RandomNumberGenerator.GetBytes(32);
        var registration = PasskeyAuthenticator.Register(
            "example.com",
            challenge,
            PasskeyAlgorithm.Es256,
            "https://example.com");
        var platform = new FakePlatformAuthenticator(registration);
        var store = new InMemoryPasskeyStore();
        var service = new PasskeyService(store, platform);

        var entry = await service.CreateWithPlatformAuthenticatorAsync(
            new PasskeyCreateRequest(
                "example.com",
                challenge,
                Encoding.UTF8.GetBytes("user"),
                "alice@example.com",
                RpName: "Example",
                UserDisplayName: "Alice",
                Origin: "https://example.com"),
            PasskeyModes.WindowsHello);
        platform.Entry = entry;

        Assert.Equal(PasskeyModes.WindowsHello, entry.PasskeyMode);
        Assert.Empty(entry.PrivateKeyAlias);
        Assert.Equal(registration.CredentialIdBase64Url, entry.CredentialId);
        Assert.Equal(0, store.PrivateKeyResolutionCount);

        var assertion = await service.AssertAsync(
            entry.CredentialId,
            RandomNumberGenerator.GetBytes(32),
            "example.com",
            "https://example.com");

        Assert.NotNull(assertion);
        Assert.Equal(1, platform.AssertionCount);
        Assert.Equal(1, store.MarkUsedCount);
        Assert.True(NativePasskeyValidation.VerifySignature(
            entry.PublicKeyAlgorithm, entry.PublicKey,
            [.. assertion!.AuthenticatorData, .. assertion.ClientData.ClientDataHash], assertion.Signature));
    }

    private sealed class FakePlatformAuthenticator(PasskeyRegistration registration) : INativePasskeyAuthenticator
    {
        private readonly PasskeyRegistration _registration = registration;
        public PasskeyEntry? Entry { get; set; }
        public bool IsAvailable => true;
        public bool IsUserVerifyingPlatformAuthenticatorAvailable => true;
        public int AssertionCount { get; private set; }
        public byte[]? LastChallenge { get; private set; }

        public Task<NativePasskeyRegistration> CreateAsync(NativePasskeyCreateRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NativePasskeyRegistration(
                _registration.CredentialId,
                _registration.AuthenticatorData,
                _registration.AttestationObject,
                _registration.ClientData.Json,
                "internal",
                request.Discoverable,
                true));

        public Task<NativePasskeyAssertion> GetAssertionAsync(NativePasskeyAssertionRequest request, CancellationToken cancellationToken = default)
        {
            var entry = Entry ?? throw new InvalidOperationException("A platform credential was not persisted.");
            LastChallenge = request.Challenge.ToArray();
            var assertion = PasskeyAuthenticator.Assert(
                entry,
                _registration.KeyMaterial.PrivateKeyPkcs8Base64,
                request.Challenge,
                request.RpId,
                request.Origin);
            using var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(_registration.KeyMaterial.PrivateKeyPkcs8Base64), out _);
            var signature = key.SignData([.. assertion.AuthenticatorData, .. assertion.ClientData.ClientDataHash],
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            AssertionCount++;
            return Task.FromResult(new NativePasskeyAssertion(
                PasskeyBase64Url.TryDecode(entry.CredentialId, out var id) ? id! : [],
                assertion.AuthenticatorData,
                signature,
                PasskeyBase64Url.TryDecode(entry.UserId, out var userId) ? userId! : [],
                assertion.ClientData.Json,
                "internal",
                true));
        }
    }

    private sealed class InMemoryPasskeyStore : IPasskeyStore
    {
        private PasskeyEntry? _entry;
        public int PrivateKeyResolutionCount { get; private set; }
        public int MarkUsedCount { get; private set; }

        public Task<long> SaveAsync(PasskeyEntry entry, string? privateKeyPkcs8Base64 = null, CancellationToken cancellationToken = default)
        {
            entry.Id = 1;
            _entry = entry;
            return Task.FromResult(1L);
        }

        public Task<PasskeyEntry?> GetAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(_entry);

        public Task<PasskeyEntry?> FindAsync(string credentialId, string? rpId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entry is not null && PasskeyCredentialId.Normalize(_entry.CredentialId) == PasskeyCredentialId.Normalize(credentialId)
                && (rpId is null || PasskeyRpId.IsEquivalent(_entry.RpId, rpId)) ? _entry : null);

        public Task<IReadOnlyList<PasskeyEntry>> ListByRpIdAsync(string rpId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PasskeyEntry>>(_entry is not null && PasskeyRpId.IsEquivalent(_entry.RpId, rpId) ? [_entry] : []);

        public Task<string?> ResolvePrivateKeyAsync(PasskeyEntry entry, CancellationToken cancellationToken = default)
        {
            PrivateKeyResolutionCount++;
            return Task.FromResult<string?>(null);
        }

        public Task MarkUsedAsync(long id, CancellationToken cancellationToken = default)
        {
            MarkUsedCount++;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var deleted = _entry?.Id == id;
            _entry = null;
            return Task.FromResult(deleted);
        }
    }
}
