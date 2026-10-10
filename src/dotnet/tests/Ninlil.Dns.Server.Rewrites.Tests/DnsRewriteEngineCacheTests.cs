using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Rewrites.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class covers how <see cref="DnsRewriteEngine" /> reads from
///     <see cref="DnsRewriteCache" /> and <see cref="DnsClientDirectory" />: a saved change
///     applies to the very next query (read-your-own-writes), an unsignalled change does not,
///     fresh copies cost no database scope, and a database that stops answering leaves the last
///     copy in use. The tests reuse the in-memory fixture of <see cref="DnsRewriteEngineTests" />
///     and raise the change signal the way a committed save does.
/// </summary>
public class DnsRewriteEngineCacheTests
{
    private static readonly IPAddress ClientAddress = IPAddress.Parse("192.0.2.10");

    private static async Task<string> AnswerAsync(
        DnsRewriteEngine engine,
        string name,
        IPAddress remoteAddress = null
    )
    {
        DnsMessage response = await engine.TryBuildResponseAsync(
            DnsRewriteEngineTests.BuildRequest(name, DnsResourceRecordTypeEnum.A),
            remoteAddress
        );

        return response is null ? null : new IPAddress(response.Answers[0].Data).ToString();
    }

    [Fact]
    public async Task TryBuildResponseAsync_ARewriteAddedAfterTheFirstQuery_AppliesToTheNextQuery()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("first.example.com", "203.0.113.1");
        Assert.Null(await AnswerAsync(fixture.Engine, "second.example.com"));

        fixture.Add("second.example.com", "203.0.113.2");

        Assert.Equal("203.0.113.2", await AnswerAsync(fixture.Engine, "second.example.com"));
    }

    [Fact]
    public async Task TryBuildResponseAsync_ARewriteRemovedOrDisabled_StopsAnsweringAtOnce()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        DnsRewriteEntity removed = fixture.Add("gone.example.com", "203.0.113.1");
        DnsRewriteEntity disabled = fixture.Add("off.example.com", "203.0.113.2");
        Assert.Equal("203.0.113.1", await AnswerAsync(fixture.Engine, "gone.example.com"));
        Assert.Equal("203.0.113.2", await AnswerAsync(fixture.Engine, "off.example.com"));

        fixture.Rewrites.Items.Remove(removed);
        fixture.Rewrites.Items.Remove(disabled);
        disabled.IsDisabled = true;
        fixture.Changed();

        Assert.Null(await AnswerAsync(fixture.Engine, "gone.example.com"));
        Assert.Null(await AnswerAsync(fixture.Engine, "off.example.com"));
    }

    [Fact]
    public async Task TryBuildResponseAsync_AChangeWithoutASignal_KeepsUsingTheCopy()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.1");
        Assert.Equal("203.0.113.1", await AnswerAsync(fixture.Engine, "host.example.com"));

        fixture.Rewrites.Items.Clear();

        Assert.Equal("203.0.113.1", await AnswerAsync(fixture.Engine, "host.example.com"));

        fixture.Changed();

        Assert.Null(await AnswerAsync(fixture.Engine, "host.example.com"));
    }

    [Fact]
    public async Task TryBuildResponseAsync_AClientMovedToAnotherGroup_ChangesTheGroupScopedAnswer()
    {
        Guid groupOne = Guid.NewGuid();
        Guid groupTwo = Guid.NewGuid();
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        DnsRewriteEngineTests.Fixture fixture = new(resolver);
        fixture.AddGroupScoped("host.example.com", "203.0.113.1", groupOne);
        fixture.AddGroupScoped("other.example.com", "203.0.113.2", groupTwo);
        DnsClientEntity client = fixture.AddClient("aa:bb:cc:dd:ee:01", groupOne);
        Assert.Equal(
            "203.0.113.1",
            await AnswerAsync(fixture.Engine, "host.example.com", ClientAddress)
        );
        Assert.Null(await AnswerAsync(fixture.Engine, "other.example.com", ClientAddress));

        client.ClientGroupId = groupTwo;
        fixture.Changed();

        Assert.Null(await AnswerAsync(fixture.Engine, "host.example.com", ClientAddress));
        Assert.Equal(
            "203.0.113.2",
            await AnswerAsync(fixture.Engine, "other.example.com", ClientAddress)
        );
    }

    [Fact]
    public async Task TryBuildResponseAsync_ADisabledClient_NoLongerGetsItsScopedRewrite()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver().WithResolution(
            ClientAddress,
            new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
        );
        DnsRewriteEngineTests.Fixture fixture = new(resolver);
        DnsClientEntity client = fixture.AddClient("aa:bb:cc:dd:ee:01");
        fixture.AddClientScoped("host.example.com", "203.0.113.1", client.Id);
        Assert.Equal(
            "203.0.113.1",
            await AnswerAsync(fixture.Engine, "host.example.com", ClientAddress)
        );

        client.IsDisabled = true;
        fixture.Changed();

        Assert.Null(await AnswerAsync(fixture.Engine, "host.example.com", ClientAddress));
    }

    [Fact]
    public async Task TryBuildResponseAsync_WithFreshCopies_OpensNoScope()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.1");
        fixture.Add("*.wild.example.com", "203.0.113.2");
        CountingScopeFactory scopes = new(fixture.ScopeFactory);
        DnsRewriteEngine engine = new(
            new DnsRewriteCache(scopes, fixture.Notifier, NullLogger<DnsRewriteCache>.Instance),
            new StaticOptionsMonitor<DnsRewriteOptions>(fixture.Options),
            NullLogger<DnsRewriteEngine>.Instance
        );

        Assert.Equal("203.0.113.1", await AnswerAsync(engine, "host.example.com"));
        int afterFirst = scopes.Count;

        for (int index = 0; index < 20; index++)
        {
            Assert.Equal("203.0.113.1", await AnswerAsync(engine, "host.example.com"));
            Assert.Equal("203.0.113.2", await AnswerAsync(engine, "a.wild.example.com"));
            Assert.Null(await AnswerAsync(engine, "none.example.com"));
        }

        Assert.True(afterFirst > 0);
        Assert.Equal(afterFirst, scopes.Count);
    }

    [Fact]
    public async Task TryBuildResponseAsync_WhenTheDatabaseStopsAnswering_KeepsAnsweringFromTheLastCopy()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("host.example.com", "203.0.113.1");
        CountingScopeFactory scopes = new(fixture.ScopeFactory);
        DnsRewriteEngine engine = new(
            new DnsRewriteCache(scopes, fixture.Notifier, NullLogger<DnsRewriteCache>.Instance),
            new StaticOptionsMonitor<DnsRewriteOptions>(fixture.Options),
            NullLogger<DnsRewriteEngine>.Instance
        );
        Assert.Equal("203.0.113.1", await AnswerAsync(engine, "host.example.com"));

        scopes.Fail = true;

        // A signal makes the next query try a reload; the failure keeps the last copy.
        fixture.Notifier.NotifyChanged([typeof(DnsRewriteEntity)]);

        Assert.Equal("203.0.113.1", await AnswerAsync(engine, "host.example.com"));
        Assert.Equal("203.0.113.1", await AnswerAsync(engine, "host.example.com"));
    }

    /// <summary>
    ///     This class wraps an <see cref="IServiceScopeFactory" />, counting the scopes opened and
    ///     optionally failing like a database that is down.
    /// </summary>
    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _count;

        public bool Fail;

        public int Count => Volatile.Read(ref _count);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _count);

            return Fail
                ? throw new InvalidOperationException("The database is down.")
                : inner.CreateScope();
        }
    }
}
