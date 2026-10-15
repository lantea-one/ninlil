using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class tests that <see cref="DnssecKeyManagementService.StartRolloverAsync" /> is
///     atomic per zone: concurrent starts produce one rollover, the new key is saved once in its
///     rollover state and never as a second Active key, and a failed save releases the gate. The
///     tests use a thread-safe in-memory repository whose <c>ListByZoneIdAsync</c> can be held at
///     a rendezvous, so the race is forced by a barrier and not by timing: without the gate both
///     starts read the zone before either inserts.
/// </summary>
public sealed class DnssecKeyManagementServiceRolloverConcurrencyTests
{
    /// <summary>
    ///     This field contains the longest a held read waits for another caller to arrive. With
    ///     the gate no other caller can arrive, so the wait ends by this limit and the outcome
    ///     does not depend on it; without the gate the second caller arrives and ends the wait at
    ///     once.
    /// </summary>
    private static readonly TimeSpan RendezvousLimit = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This class is a thread-safe in-memory <see cref="IDnssecKeyRepository" /> that calls a
    ///     hook after <c>ListByZoneIdAsync</c> took its snapshot, so a test can hold the caller
    ///     with stale data while another caller runs.
    /// </summary>
    private sealed class HookedKeyRepository : IDnssecKeyRepository
    {
        /// <summary>
        ///     This field guards <see cref="_items" />.
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        ///     This field contains the stored keys.
        /// </summary>
        private readonly List<DnssecKeyEntity> _items = [];

        /// <summary>
        ///     This property contains the hook awaited by <c>ListByZoneIdAsync</c> after the
        ///     snapshot is taken and before it returns. <see langword="null" /> means no hold.
        /// </summary>
        public Func<Guid, Task> AfterListSnapshot { get; set; }

        /// <summary>
        ///     This method returns a copy of the stored keys.
        /// </summary>
        public List<DnssecKeyEntity> Snapshot()
        {
            lock (_lock)
                return [.. _items];
        }

        /// <inheritdoc />
        public Task<DnssecKeyEntity> GetByIdAsync(
            Guid id,
            CancellationToken stoppingToken = default
        )
        {
            lock (_lock)
                return Task.FromResult(_items.FirstOrDefault(item => item.Id == id));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<DnssecKeyEntity>> ListAsync(
            System.Linq.Expressions.Expression<Func<DnssecKeyEntity, bool>> predicate = null,
            CancellationToken stoppingToken = default
        )
        {
            lock (_lock)
                return Task.FromResult<IReadOnlyList<DnssecKeyEntity>>(
                    predicate is null ? [.. _items] : [.. _items.Where(predicate.Compile())]
                );
        }

        /// <inheritdoc />
        public Task AddAsync(DnssecKeyEntity entity, CancellationToken stoppingToken = default)
        {
            lock (_lock)
                _items.Add(entity);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Update(DnssecKeyEntity entity) { }

        /// <inheritdoc />
        public void Remove(DnssecKeyEntity entity)
        {
            lock (_lock)
                _items.Remove(entity);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        )
        {
            List<DnssecKeyEntity> snapshot;

            lock (_lock)
                snapshot = [.. _items.Where(item => item.ZoneId == zoneId)];

            if (AfterListSnapshot is not null)
                await AfterListSnapshot(zoneId);

            return snapshot;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
            Guid zoneId,
            int skip,
            int take,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<int> CountByZoneIdAsync(
            Guid zoneId,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();
    }

    /// <summary>
    ///     This class is a unit of work that runs a callback on every save, so a test can record
    ///     the repository state at that moment or make the save fail.
    /// </summary>
    /// <param name="onSave">The callback run by every <c>SaveChangesAsync</c>.</param>
    private sealed class CallbackUnitOfWork(Action onSave) : IUnitOfWork
    {
        /// <inheritdoc />
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
        {
            onSave();

            return Task.FromResult(1);
        }
    }

    /// <summary>
    ///     This method builds a service over the shared repository and gate.
    /// </summary>
    private static DnssecKeyManagementService CreateService(
        HookedKeyRepository repository,
        IUnitOfWork unitOfWork,
        DnssecZoneKeyGate gate
    ) =>
        new(
            repository,
            unitOfWork,
            new EphemeralDataProtectionProvider(),
            algorithmPolicy: null,
            zoneKeyGate: gate
        );

    /// <summary>
    ///     This method adds an Active key of <paramref name="role" /> to the zone and returns it.
    /// </summary>
    private static async Task<DnssecKeyEntity> SeedActiveKeyAsync(
        HookedKeyRepository repository,
        Guid zoneId,
        DnssecKeyRoleEnum role
    )
    {
        DnssecKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Role = role,
            Algorithm = DnssecAlgorithmEnum.Ed25519,
            LifecycleState = DnssecKeyLifecycleStateEnum.Active,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await repository.AddAsync(key);

        return key;
    }

    [Fact]
    public async Task StartRolloverAsync_TwoConcurrentStartsForTheSameZone_ProduceExactlyOneRollover()
    {
        Guid zoneId = Guid.NewGuid();
        HookedKeyRepository repository = new();
        DnssecZoneKeyGate gate = new();
        DnssecKeyEntity active = await SeedActiveKeyAsync(
            repository,
            zoneId,
            DnssecKeyRoleEnum.Zsk
        );

        // Holds the first reader until the second one has read too (or the limit passes), so
        // both starts decide on the same stale zone state unless the gate keeps the second out.
        TaskCompletionSource secondArrived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int reads = 0;
        repository.AfterListSnapshot = async _ =>
        {
            if (Interlocked.Increment(ref reads) == 1)
                await Task.WhenAny(secondArrived.Task, Task.Delay(RendezvousLimit));
            else
                secondArrived.TrySetResult();
        };

        DnssecKeyManagementService first = CreateService(
            repository,
            new CallbackUnitOfWork(() => { }),
            gate
        );
        DnssecKeyManagementService second = CreateService(
            repository,
            new CallbackUnitOfWork(() => { }),
            gate
        );

        Task<DnssecKeyEntity> firstStart = Task.Run(() =>
            first.StartRolloverAsync(zoneId, active.Id)
        );
        Task<DnssecKeyEntity> secondStart = Task.Run(() =>
            second.StartRolloverAsync(zoneId, active.Id)
        );

        // Waits for both without throwing, so the outcome of each can be inspected.
        await Task.WhenAll(
            firstStart.ContinueWith(_ => { }, TaskScheduler.Default),
            secondStart.ContinueWith(_ => { }, TaskScheduler.Default)
        );

        Task[] tasks = [firstStart, secondStart];
        int succeeded = tasks.Count(task => task.IsCompletedSuccessfully);
        Task failed = tasks.SingleOrDefault(task => task.IsFaulted);

        Assert.Equal(1, succeeded);
        Assert.NotNull(failed);
        Assert.IsType<ConflictException>(failed.Exception!.InnerException);
        Assert.Single(
            repository.Snapshot(),
            key => key.LifecycleState == DnssecKeyLifecycleStateEnum.Published
        );
        Assert.Single(
            repository.Snapshot(),
            key => key.LifecycleState == DnssecKeyLifecycleStateEnum.Active
        );
    }

    [Theory]
    [InlineData(DnssecKeyRoleEnum.Zsk, DnssecKeyLifecycleStateEnum.Published)]
    [InlineData(DnssecKeyRoleEnum.Ksk, DnssecKeyLifecycleStateEnum.DoubleSignature)]
    public async Task StartRolloverAsync_SavesTheIncomingKeyOnceAndNeverAsASecondActiveKey(
        DnssecKeyRoleEnum role,
        DnssecKeyLifecycleStateEnum expectedState
    )
    {
        Guid zoneId = Guid.NewGuid();
        HookedKeyRepository repository = new();
        DnssecKeyEntity active = await SeedActiveKeyAsync(repository, zoneId, role);

        // The key states of the zone as they are at each save.
        List<DnssecKeyLifecycleStateEnum[]> savedStates = [];
        CallbackUnitOfWork unitOfWork = new(() =>
            savedStates.Add([
                .. repository
                    .Snapshot()
                    .Where(key => key.Role == role)
                    .Select(key => key.LifecycleState),
            ])
        );
        DnssecKeyManagementService service = CreateService(
            repository,
            unitOfWork,
            new DnssecZoneKeyGate()
        );

        DnssecKeyEntity incoming = await service.StartRolloverAsync(zoneId, active.Id);

        Assert.Equal(expectedState, incoming.LifecycleState);
        DnssecKeyLifecycleStateEnum[] onlySave = Assert.Single(savedStates);
        Assert.Equal(1, onlySave.Count(state => state == DnssecKeyLifecycleStateEnum.Active));
    }

    [Fact]
    public async Task StartRolloverAsync_WhenTheSaveFails_AddsNoKeyAndReleasesTheGate()
    {
        Guid zoneId = Guid.NewGuid();
        HookedKeyRepository repository = new();
        DnssecZoneKeyGate gate = new();
        DnssecKeyEntity active = await SeedActiveKeyAsync(
            repository,
            zoneId,
            DnssecKeyRoleEnum.Zsk
        );

        // The first save throws, as a database error would; later saves succeed. A failed save
        // has persisted nothing, so the fake removes the added key, as the unit of work would
        // discard it.
        int saves = 0;
        DnssecKeyManagementService service = CreateService(
            repository,
            new CallbackUnitOfWork(() =>
            {
                if (++saves != 1)
                    return;

                foreach (
                    DnssecKeyEntity added in repository.Snapshot().Where(key => key.Id != active.Id)
                )
                    repository.Remove(added);

                throw new InvalidOperationException("The database is unavailable.");
            }),
            gate
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartRolloverAsync(zoneId, active.Id)
        );
        Assert.Single(repository.Snapshot());

        // A held gate would make this second start wait forever.
        DnssecKeyEntity incoming = await service
            .StartRolloverAsync(zoneId, active.Id)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(DnssecKeyLifecycleStateEnum.Published, incoming.LifecycleState);
    }

    [Fact]
    public async Task StartRolloverAsync_ForTwoDifferentZones_RunsBothAtTheSameTime()
    {
        HookedKeyRepository repository = new();
        DnssecZoneKeyGate gate = new();
        Guid zoneA = Guid.NewGuid();
        Guid zoneB = Guid.NewGuid();
        DnssecKeyEntity activeA = await SeedActiveKeyAsync(
            repository,
            zoneA,
            DnssecKeyRoleEnum.Zsk
        );
        DnssecKeyEntity activeB = await SeedActiveKeyAsync(
            repository,
            zoneB,
            DnssecKeyRoleEnum.Zsk
        );

        // A barrier of two: each start waits in its read until the other zone's start is in its
        // read too. A gate shared across zones would never let the second one in, and the
        // barrier would time out.
        using Barrier bothInside = new(2);
        repository.AfterListSnapshot = _ =>
            Task.Run(() => Assert.True(bothInside.SignalAndWait(TimeSpan.FromSeconds(30))));

        DnssecKeyManagementService serviceA = CreateService(
            repository,
            new CallbackUnitOfWork(() => { }),
            gate
        );
        DnssecKeyManagementService serviceB = CreateService(
            repository,
            new CallbackUnitOfWork(() => { }),
            gate
        );

        DnssecKeyEntity[] incoming = await Task.WhenAll(
            Task.Run(() => serviceA.StartRolloverAsync(zoneA, activeA.Id)),
            Task.Run(() => serviceB.StartRolloverAsync(zoneB, activeB.Id))
        );

        Assert.All(
            incoming,
            key => Assert.Equal(DnssecKeyLifecycleStateEnum.Published, key.LifecycleState)
        );
    }
}
