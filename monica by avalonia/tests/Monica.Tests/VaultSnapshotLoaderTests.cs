using System.Diagnostics;
using System.Reflection;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data.Repositories;
using Xunit.Abstractions;

namespace Monica.Tests;

public sealed class VaultSnapshotLoaderTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Vault_snapshot_loader_fans_out_reads_after_password_snapshot()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, DelayedVaultRepositoryProxy>();
        var probe = (DelayedVaultRepositoryProxy)(object)repository;
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await VaultSnapshotLoader.LoadAsync(repository);

        stopwatch.Stop();
        output.WriteLine(
            $"snapshot={stopwatch.Elapsed.TotalMilliseconds:F3} ms, " +
            $"reads={probe.ReadCallCount}, maxConcurrency={probe.MaxConcurrentReadCount}");
        Assert.Single(snapshot.ActivePasswords);
        Assert.Equal(6, probe.ReadCallCount);
        Assert.True(probe.AllPostPasswordReadsStartedAfterPassword);
        Assert.True(
            probe.MaxConcurrentReadCount >= 5,
            $"Expected five post-password reads to overlap, but observed {probe.MaxConcurrentReadCount} concurrent read(s).");
        Assert.True(
            stopwatch.ElapsedMilliseconds < 450,
            $"Six simulated 100 ms reads took {stopwatch.ElapsedMilliseconds} ms; expected one password phase plus one parallel fan-out phase.");
    }

    [Fact]
    public async Task Vault_snapshot_loader_bounds_quick_access_before_snapshot_retention()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, DelayedVaultRepositoryProxy>();
        var probe = (DelayedVaultRepositoryProxy)(object)repository;
        var now = DateTimeOffset.UtcNow;
        probe.QuickAccessRecords =
        [
            .. Enumerable.Range(1, 6).Select(index => new PasswordQuickAccessRecord
            {
                PasswordId = index,
                OpenCount = 100 - index,
                LastOpenedAt = now.AddDays(-index)
            }),
            .. Enumerable.Range(7, 12).Select(index => new PasswordQuickAccessRecord
            {
                PasswordId = index,
                OpenCount = 1,
                LastOpenedAt = now.AddMinutes(index)
            })
        ];

        var snapshot = await VaultSnapshotLoader.LoadAsync(repository);

        Assert.Equal(12, snapshot.PasswordQuickAccessRecords.Count);
        Assert.Equal(
            [1L, 2L, 3L, 4L, 5L, 6L, 13L, 14L, 15L, 16L, 17L, 18L],
            snapshot.PasswordQuickAccessRecords.Keys.Order().ToArray());
    }

    [Fact]
    public async Task Vault_snapshot_loader_skips_eager_custom_fields()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, DelayedVaultRepositoryProxy>();
        var probe = (DelayedVaultRepositoryProxy)(object)repository;

        await VaultSnapshotLoader.LoadAsync(repository);

        Assert.Equal(0, probe.CustomFieldReadCallCount);
    }

    [Fact]
    public async Task Vault_snapshot_loader_skips_eager_password_attachments()
    {
        var repository = DispatchProxy.Create<IMonicaRepository, DelayedVaultRepositoryProxy>();
        var probe = (DelayedVaultRepositoryProxy)(object)repository;

        await VaultSnapshotLoader.LoadAsync(repository);

        Assert.Equal(0, probe.AttachmentReadCallCount);
        Assert.Equal(1, probe.AttachmentOwnerReadCallCount);
    }

    public class DelayedVaultRepositoryProxy : DispatchProxy
    {
        private static readonly TimeSpan ReadDelay = TimeSpan.FromMilliseconds(100);
        private int _activeReadCount;
        private int _maxConcurrentReadCount;
        private int _readCallCount;
        private int _passwordReadCompleted;
        private int _allPostPasswordReadsStartedAfterPassword = 1;

        public int MaxConcurrentReadCount => Volatile.Read(ref _maxConcurrentReadCount);
        public int ReadCallCount => Volatile.Read(ref _readCallCount);
        public int CustomFieldReadCallCount { get; private set; }
        public int AttachmentReadCallCount { get; private set; }
        public int AttachmentOwnerReadCallCount { get; private set; }
        public IReadOnlyList<PasswordQuickAccessRecord> QuickAccessRecords { get; set; } = [];
        public bool AllPostPasswordReadsStartedAfterPassword =>
            Volatile.Read(ref _allPostPasswordReadsStartedAfterPassword) == 1;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            var cancellationToken = args?
                .OfType<CancellationToken>()
                .LastOrDefault() ?? CancellationToken.None;

            return targetMethod.Name switch
            {
                nameof(IMonicaRepository.GetPasswordsAsync) => DelayPasswordReadAsync(cancellationToken),
                nameof(IMonicaRepository.GetCustomFieldsByEntryIdsAsync) => DelayCustomFieldReadAsync(cancellationToken),
                nameof(IMonicaRepository.GetAttachmentsByOwnerIdsAsync) => DelayAttachmentReadAsync(cancellationToken),
                nameof(IMonicaRepository.GetAttachmentOwnerIdsAsync) => DelayAttachmentOwnerReadAsync(cancellationToken),
                nameof(IMonicaRepository.GetSecureItemsAsync) => DelayPostPasswordReadAsync<IReadOnlyList<SecureItem>>(
                    [],
                    cancellationToken),
                nameof(IMonicaRepository.GetCategoriesAsync) => DelayPostPasswordReadAsync<IReadOnlyList<Category>>(
                    [],
                    cancellationToken),
                nameof(IMonicaRepository.GetPasswordQuickAccessRecordsAsync) =>
                    DelayPostPasswordReadAsync(QuickAccessRecords, cancellationToken),
                nameof(IMonicaRepository.GetMdbxDatabasesAsync) =>
                    DelayPostPasswordReadAsync<IReadOnlyList<LocalMdbxDatabase>>([], cancellationToken),
                _ => throw new NotSupportedException($"Unexpected repository call: {targetMethod.Name}")
            };
        }

        private Task<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>> DelayCustomFieldReadAsync(
            CancellationToken cancellationToken)
        {
            CustomFieldReadCallCount++;
            return DelayPostPasswordReadAsync<IReadOnlyDictionary<long, IReadOnlyList<CustomField>>>(
                new Dictionary<long, IReadOnlyList<CustomField>>(),
                cancellationToken);
        }

        private Task<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>> DelayAttachmentReadAsync(
            CancellationToken cancellationToken)
        {
            AttachmentReadCallCount++;
            return DelayPostPasswordReadAsync<IReadOnlyDictionary<long, IReadOnlyList<Attachment>>>(
                new Dictionary<long, IReadOnlyList<Attachment>>(),
                cancellationToken);
        }

        private Task<IReadOnlyList<long>> DelayAttachmentOwnerReadAsync(CancellationToken cancellationToken)
        {
            AttachmentOwnerReadCallCount++;
            return DelayPostPasswordReadAsync<IReadOnlyList<long>>([1], cancellationToken);
        }

        private async Task<IReadOnlyList<PasswordEntry>> DelayPasswordReadAsync(CancellationToken cancellationToken)
        {
            var result = await DelayReadAsync<IReadOnlyList<PasswordEntry>>(
                [new PasswordEntry { Id = 1, Title = "Baseline" }],
                cancellationToken);
            Volatile.Write(ref _passwordReadCompleted, 1);
            return result;
        }

        private Task<T> DelayPostPasswordReadAsync<T>(T result, CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _passwordReadCompleted) == 0)
            {
                Volatile.Write(ref _allPostPasswordReadsStartedAfterPassword, 0);
            }

            return DelayReadAsync(result, cancellationToken);
        }

        private async Task<T> DelayReadAsync<T>(T result, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _readCallCount);
            var activeReadCount = Interlocked.Increment(ref _activeReadCount);
            UpdateMaximumConcurrentReads(activeReadCount);
            try
            {
                await Task.Delay(ReadDelay, cancellationToken);
                return result;
            }
            finally
            {
                Interlocked.Decrement(ref _activeReadCount);
            }
        }

        private void UpdateMaximumConcurrentReads(int candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maxConcurrentReadCount);
                if (candidate <= current ||
                    Interlocked.CompareExchange(ref _maxConcurrentReadCount, candidate, current) == current)
                {
                    return;
                }
            }
        }
    }
}
