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

public sealed class DnsClientScheduleEngineTests
{
    private static readonly IPAddress ClientAddress = IPAddress.Parse("203.0.113.10");
    private static readonly ClientIdentityResolution Resolution = new(
        "aa:bb:cc:dd:ee:ff",
        InternetProtocolVersionEnum.IPv4
    );

    // A Monday 22:00 in "Asia/Tokyo" (UTC+9) -- deliberately not this sandbox's own local time
    // zone, the same "genuinely different zone" proof DnsScheduleWindowEvaluatorTests establishes.
    private static readonly DateTimeOffset MondayNightTokyo = DateTimeOffset.Parse(
        "2026-09-14T13:00:00Z"
    );

    // A Tuesday 12:00 in "Asia/Tokyo" -- well outside any of this file's own school-night windows.
    private static readonly DateTimeOffset TuesdayMiddayTokyo = DateTimeOffset.Parse(
        "2026-09-15T03:00:00Z"
    );

    private sealed class Fixture
    {
        public readonly InMemoryDnsClientRepository Clients = new();
        public readonly InMemoryDnsClientGroupRepository Groups = new();
        public readonly InMemoryDnsClientScheduleRepository ClientSchedules = new();
        public readonly InMemoryDnsClientGroupScheduleRepository GroupSchedules = new();
        public readonly DnsClientScheduleEngine Engine;

        /// <param name="identityResolver">
        ///     The resolver to inject, or <see langword="null" /> to prove the "no infrastructure
        ///     at all" regression.
        /// </param>
        /// <param name="registerGroupRepository">
        ///     Whether to register <see cref="IDnsClientGroupRepository" /> at all -- defaults to
        ///     <see langword="true" />; set <see langword="false" /> to prove a group-pause check
        ///     never crashes when that one dependency alone is absent (mirroring how the group
        ///     schedule repository is already allowed to be absent above).
        /// </param>
        public Fixture(
            IClientIdentityResolver identityResolver = null,
            bool registerGroupRepository = true
        )
        {
            ServiceCollection services = new();
            services.AddSingleton<IDnsClientRepository>(Clients);
            services.AddSingleton<IDnsClientScheduleRepository>(ClientSchedules);
            services.AddSingleton<IDnsClientGroupScheduleRepository>(GroupSchedules);

            if (registerGroupRepository)
                services.AddSingleton<IDnsClientGroupRepository>(Groups);

            IServiceScopeFactory scopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            Engine = new DnsClientScheduleEngine(
                scopeFactory,
                NullLogger<DnsClientScheduleEngine>.Instance,
                identityResolver
            );
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
            Clients.Addresses.Add(
                (Resolution.ClientIdentifier, Resolution.ProtocolVersion, client.Id)
            );
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
            return group;
        }

        public void AddClientSchedule(
            Guid clientId,
            DnsScheduleDayOfWeekEnum days,
            TimeOnly start,
            TimeOnly end,
            string timeZoneId = "Asia/Tokyo",
            bool isDisabled = false
        ) =>
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

        public void AddGroupSchedule(
            Guid groupId,
            DnsScheduleDayOfWeekEnum days,
            TimeOnly start,
            TimeOnly end,
            string timeZoneId = "Asia/Tokyo",
            bool isDisabled = false
        ) =>
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
    ///     This test proves a client's own direct schedule and its group's own schedule are
    ///     independently checked -- either one being active blocks the query, a union rather than
    ///     one overriding the other. Here, only the group's own schedule is active (the client's
    ///     own direct schedule covers a completely different, non-matching day), and the query is
    ///     still blocked.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_ClientAndGroupScheduleBothPresent_EitherOneBlocksIndependently()
    {
        Guid groupId = Guid.NewGuid();
        Fixture fixture = new(new FakeClientIdentityResolver(ClientAddress, Resolution));
        DnsClientEntity client = fixture.AddClient(groupId);

        // The client's own direct schedule only ever covers Saturday -- never active on the
        // Monday night this test evaluates against.
        fixture.AddClientSchedule(
            client.Id,
            DnsScheduleDayOfWeekEnum.Saturday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0)
        );

        // The group's own schedule covers the school nights that do include Monday.
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
    ///     This test proves the engine never blocks anything at all when no
    ///     <see cref="IClientIdentityResolver" /> was supplied -- the identical "cross-cutting
    ///     infrastructure absent, whole path silently skipped" regression
    ///     <c>Ninlil.Dns.Server.Filtering.Tests.DnsFilterEngineTests</c> already proves for
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
    ///     This test proves a client with a future <see cref="DnsClientEntity.PausedUntilUtc" /> is
    ///     blocked regardless of any schedule state that would otherwise allow the query -- here,
    ///     evaluated at a time no schedule of this client's own would ever block on its own.
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
    ///     This test proves a group-level pause blocks every member -- the resolved client itself
    ///     carries no pause of its own at all, only its group does.
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
    ///     This test proves pausing one client does not affect a genuinely different, unrelated
    ///     client -- two distinct client identifiers, resolved from two distinct source
    ///     addresses, only the first one paused.
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
        fixture.Clients.Addresses.Add(
            (
                unrelatedResolution.ClientIdentifier,
                unrelatedResolution.ProtocolVersion,
                unrelatedClient.Id
            )
        );

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
    ///     This test proves clearing a client's own <see cref="DnsClientEntity.PausedUntilUtc" />
    ///     back to <see langword="null" /> (what <c>ClientsController.Resume</c> actually does)
    ///     genuinely un-blocks the very next evaluation, immediately -- there is no cached/stale
    ///     decision left behind anywhere in this engine.
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

        // Simulate ClientsController.Resume's own effect directly against the same tracked entity.
        client.PausedUntilUtc = null;

        DnsClientScheduleDecision resumedDecision = await fixture.Engine.EvaluateAsync(
            ClientAddress,
            TuesdayMiddayTokyo
        );
        Assert.False(resumedDecision.IsBlocked);
    }

    /// <summary>
    ///     This test proves an active pause always wins over an active schedule that would
    ///     otherwise also block the same query -- not load-bearing for the wire-level answer
    ///     (both paths answer identically), but load-bearing for <see cref="DnsClientScheduleDecision.Reason" />,
    ///     which this test uses to confirm the pause check genuinely ran first.
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
    ///     This test proves a group-pause check never crashes when
    ///     <see cref="IDnsClientGroupRepository" /> alone is absent from dependency injection --
    ///     the same optional-dependency discipline every other cross-cutting engine in this
    ///     project already follows, mirroring <see cref="EvaluateAsync_NoIdentityResolver_NeverBlocksAnything" />
    ///     for this one additional, independently optional dependency.
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
