using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClientSchedule.Tests.Fakes;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

/// <summary>
///     This class tests <see cref="DnsClientScheduleEngine" />: client and group schedules,
///     disabled schedules and clients, client and group pauses, their order, and missing optional
///     dependencies. The engine runs against in-memory repositories in a small service provider
///     and a fake identity resolver; times are fixed instants evaluated in "Asia/Tokyo".
/// </summary>
public sealed class DnsClientScheduleEngineTests
{
    private static readonly IPAddress ClientAddress = IPAddress.Parse("203.0.113.10");
    private static readonly ClientIdentityResolution Resolution = new(
        "aa:bb:cc:dd:ee:ff",
        InternetProtocolVersionEnum.IPv4
    );

    // Monday 22:00 in "Asia/Tokyo" (UTC+9), a zone other than the host's (see
    // DnsScheduleWindowEvaluatorTests).
    private static readonly DateTimeOffset MondayNightTokyo = DateTimeOffset.Parse(
        "2026-09-14T13:00:00Z"
    );

    // Tuesday 12:00 in "Asia/Tokyo", outside every school-night window in this file.
    private static readonly DateTimeOffset TuesdayMiddayTokyo = DateTimeOffset.Parse(
        "2026-09-15T03:00:00Z"
    );

    internal sealed class Fixture
    {
        public readonly InMemoryDnsClientRepository Clients = new();
        public readonly InMemoryDnsClientGroupRepository Groups = new();
        public readonly InMemoryDnsClientScheduleRepository ClientSchedules = new();
        public readonly InMemoryDnsClientGroupScheduleRepository GroupSchedules = new();
        public readonly InMemoryDnsClientAddressRepository ClientAddresses = new();
        public readonly EntityChangeNotifier Notifier = new();
        public readonly IServiceScopeFactory ScopeFactory;
        public readonly DnsClientScheduleEngine Engine;

        /// <param name="identityResolver">
        ///     The resolver to inject, or <see langword="null" /> to test the engine without one.
        /// </param>
        /// <param name="registerGroupRepository">
        ///     Whether to register <see cref="IDnsClientGroupRepository" /> (the default). Pass
        ///     <see langword="false" /> to test that the group pause check copes without it.
        /// </param>
        public Fixture(
            IClientIdentityResolver identityResolver = null,
            bool registerGroupRepository = true
        )
        {
            ServiceCollection services = new();
            services.AddSingleton<IDnsClientRepository>(Clients);
            services.AddSingleton<IDnsClientAddressRepository>(ClientAddresses);
            services.AddSingleton<IDnsClientScheduleRepository>(ClientSchedules);
            services.AddSingleton<IDnsClientGroupScheduleRepository>(GroupSchedules);

            if (registerGroupRepository)
                services.AddSingleton<IDnsClientGroupRepository>(Groups);

            IServiceScopeFactory scopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            ScopeFactory = scopeFactory;

            Engine = new DnsClientScheduleEngine(
                new DnsClientScheduleCache(
                    scopeFactory,
                    Notifier,
                    NullLogger<DnsClientScheduleCache>.Instance
                ),
                identityResolver,
                new DnsClientDirectory(
                    scopeFactory,
                    Notifier,
                    NullLogger<DnsClientDirectory>.Instance
                )
            );
        }

        /// <summary>
        ///     This method stands in for the committed save a real repository write ends with: the
        ///     in-memory repositories have no unit of work, so the fixture's setup helpers (and
        ///     tests that change a row afterwards) raise the signal themselves, for the entity
        ///     types the caches watch.
        /// </summary>
        public void Changed() =>
            Notifier.NotifyChanged([
                typeof(DnsClientEntity),
                typeof(DnsClientAddressEntity),
                typeof(DnsClientGroupEntity),
                typeof(DnsClientScheduleEntity),
                typeof(DnsClientGroupScheduleEntity),
            ]);

        /// <summary>
        ///     This method registers an identifier for a client.
        /// </summary>
        public void AddAddress(Guid clientId, ClientIdentityResolution resolution)
        {
            Clients.Addresses.Add(
                (resolution.ClientIdentifier, resolution.ProtocolVersion, clientId)
            );
            ClientAddresses.Items.Add(
                new DnsClientAddressEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    ClientIdentifier = resolution.ClientIdentifier,
                    ProtocolVersion = resolution.ProtocolVersion,
                }
            );
            Changed();
        }

        public DnsClientEntity AddClient(
            Guid? groupId = null,
            bool isDisabled = false,
            DateTimeOffset? pausedUntilUtc = null
        )
        {
            DnsClientEntity client = new()
            {
                Id = Guid.NewGuid(),
                ClientGroupId = groupId,
                IsDisabled = isDisabled,
                PausedUntilUtc = pausedUntilUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            Clients.Items.Add(client);
            AddAddress(client.Id, Resolution);
            return client;
        }

        public DnsClientGroupEntity AddGroup(Guid groupId, DateTimeOffset? pausedUntilUtc = null)
        {
            DnsClientGroupEntity group = new()
            {
                Id = groupId,
                Name = groupId.ToString(),
                PausedUntilUtc = pausedUntilUtc,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            Groups.Items.Add(group);
            Changed();
            return group;
        }

        public void AddClientSchedule(
            Guid clientId,
            DnsScheduleDayOfWeekEnum days,
            TimeOnly start,
            TimeOnly end,
            string timeZoneId = "Asia/Tokyo",
            bool isDisabled = false
        )
        {
            ClientSchedules.Items.Add(
                new DnsClientScheduleEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    DaysOfWeek = days,
                    StartTime = start,
                    EndTime = end,
                    TimeZoneId = timeZoneId,
                    IsDisabled = isDisabled,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            Changed();
        }

        public void AddGroupSchedule(
            Guid groupId,
            DnsScheduleDayOfWeekEnum days,
            TimeOnly start,
            TimeOnly end,
            string timeZoneId = "Asia/Tokyo",
            bool isDisabled = false
        )
        {
            GroupSchedules.Items.Add(
                new DnsClientGroupScheduleEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    DaysOfWeek = days,
                    StartTime = start,
                    EndTime = end,
                    TimeZoneId = timeZoneId,
                    IsDisabled = isDisabled,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
            Changed();
        }
    }

    private static DnsScheduleDayOfWeekEnum SchoolNights() =>
        DnsScheduleDayOfWeekEnum.Sunday
        | DnsScheduleDayOfWeekEnum.Monday
        | DnsScheduleDayOfWeekEnum.Tuesday
        | DnsScheduleDayOfWeekEnum.Wednesday
        | DnsScheduleDayOfWeekEnum.Thursday;

    [Fact]
    public async Task EvaluateAsync_ClientWithActiveDirectSchedule_ReturnsBlocked()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_ClientWithScheduleOutsideWindow_ReturnsAllowed()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_DisabledSchedule_ContributesNothing()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            isDisabled: true
        );

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_ClientResolvedToGroupWithActiveSchedule_ReturnsBlocked()
    {
        Guid groupId = Guid.NewGuid();
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        fixture.AddClient(groupId);
        fixture.AddGroupSchedule(groupId, SchoolNights(), new TimeOnly(21, 0), new TimeOnly(7, 0));

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.True(decision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that the client's schedules and its group's schedules are checked
    ///     independently, so either one blocks; neither overrides the other. Here only the group's
    ///     schedule is active (the client's covers another day), and the query is blocked.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ClientAndGroupScheduleBothPresent_EitherOneBlocksIndependently()
    {
        Guid groupId = Guid.NewGuid();
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient(groupId);

        // The client's schedule covers only Saturday, so it is not active on Monday night.
        fixture.AddClientSchedule(
            client.Id,
            DnsScheduleDayOfWeekEnum.Saturday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        // The group's schedule covers school nights, which include Monday.
        fixture.AddGroupSchedule(groupId, SchoolNights(), new TimeOnly(21, 0), new TimeOnly(7, 0));

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_DisabledClient_ContributesNeitherItsOwnScheduleNorItsGroups()
    {
        Guid groupId = Guid.NewGuid();
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient(groupId, isDisabled: true);
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );
        fixture.AddGroupSchedule(groupId, SchoolNights(), new TimeOnly(21, 0), new TimeOnly(7, 0));

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_UnresolvableAddress_ReturnsAllowed()
    {
        Fixture fixture = new(
            new FakeClientIdentityResolver(IPAddress.Parse("198.51.100.1"), Resolution)
        );
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that the engine blocks nothing when no
    ///     <see cref="IClientIdentityResolver" /> is supplied, as
    ///     <c>Ninlil.Dns.Server.Filtering.Tests.DnsFilterEngineTests</c> does for
    ///     <c>DnsFilterEngine</c>.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_NoIdentityResolver_NeverBlocksAnything()
    {
        Fixture fixture = new(identityResolver: null);
        DnsClientEntity client = fixture.AddClient();
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that a client with a future <see cref="DnsClientEntity.PausedUntilUtc" />
    ///     is blocked although it has no schedule that would block.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ClientWithFuturePause_ReturnsBlockedRegardlessOfSchedule()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        fixture.AddClient(pausedUntilUtc: TuesdayMiddayTokyo.AddHours(1));

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );

        Assert.True(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_ClientWithPastPause_ReturnsAllowed()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        fixture.AddClient(pausedUntilUtc: TuesdayMiddayTokyo.AddHours(-1));

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    [Fact]
    public async Task EvaluateAsync_ClientWithNullPause_ReturnsAllowed()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        fixture.AddClient(pausedUntilUtc: null);

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );

        Assert.False(decision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that a group pause blocks a member that has no pause of its own.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ClientResolvedToPausedGroup_ReturnsBlocked()
    {
        Guid groupId = Guid.NewGuid();
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        fixture.AddClient(groupId);
        fixture.AddGroup(groupId, TuesdayMiddayTokyo.AddHours(1));

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );

        Assert.True(decision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that pausing one client does not affect another: two client
    ///     identifiers, resolved from two source addresses, with only the first paused.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_PausingOneClient_DoesNotAffectAnUnrelatedClient()
    {
        IPAddress unrelatedAddress = IPAddress.Parse("198.51.100.77");
        ClientIdentityResolution unrelatedResolution = new(
            "11:22:33:44:55:66",
            InternetProtocolVersionEnum.IPv4
        );

        Fixture fixture = new(
            new FakeClientIdentityResolver(
                new Dictionary<IPAddress, ClientIdentityResolution>
                {
                    [ClientAddress] = Resolution,
                    [unrelatedAddress] = unrelatedResolution,
                }
            )
        );
        fixture.AddClient(pausedUntilUtc: TuesdayMiddayTokyo.AddHours(1));

        DnsClientEntity unrelatedClient = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        fixture.Clients.Items.Add(unrelatedClient);
        fixture.AddAddress(unrelatedClient.Id, unrelatedResolution);

        DnsClientScheduleDecision pausedClientDecision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );
        Assert.True(pausedClientDecision.IsBlocked);

        DnsClientScheduleDecision unrelatedDecision = await fixture.Engine.EvaluateAsync(
            unrelatedAddress,
            TuesdayMiddayTokyo
        );
        Assert.False(unrelatedDecision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that clearing <see cref="DnsClientEntity.PausedUntilUtc" /> (what
    ///     <c>ClientsController.Resume</c> does) unblocks the next evaluation at once: the saved
    ///     change expires the cached client.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ResumingAPausedClient_UnblocksImmediately()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient(pausedUntilUtc: TuesdayMiddayTokyo.AddHours(1));

        DnsClientScheduleDecision pausedDecision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );
        Assert.True(pausedDecision.IsBlocked);

        // Do what ClientsController.Resume does, on the same stored entity: change the row and
        // save, which raises the change signal.
        client.PausedUntilUtc = null;
        fixture.Changed();

        DnsClientScheduleDecision resumedDecision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );
        Assert.False(resumedDecision.IsBlocked);
    }

    /// <summary>
    ///     This test checks that a pause is reported ahead of an active schedule. The DNS answer
    ///     is the same either way; <see cref="DnsClientScheduleDecision.Reason" /> shows which
    ///     check ran first.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_PauseAndScheduleBothActive_PauseReasonWins()
    {
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient(pausedUntilUtc: MondayNightTokyo.AddHours(1));
        fixture.AddClientSchedule(
            client.Id,
            SchoolNights(),
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            MondayNightTokyo
        );

        Assert.True(decision.IsBlocked);
        Assert.Contains("paused", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     This test checks that a client with a group is evaluated without error, and allowed,
    ///     when <see cref="IDnsClientGroupRepository" /> is not registered: the group pause check
    ///     is skipped. The client has no pause of its own here.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_NoGroupRepository_ClientPauseStillWorksAndGroupPauseIsSkipped()
    {
        Guid groupId = Guid.NewGuid();
        Fixture fixture = new(
            new FakeClientIdentityResolver(ClientAddress, Resolution),
            registerGroupRepository: false
        );
        fixture.AddClient(groupId);

        DnsClientScheduleDecision decision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );

        Assert.False(decision.IsBlocked);
    }
}
