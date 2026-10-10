using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers <see cref="DnsClientDirectory" /> over in-memory client repositories:
///     lookup by identifier and identifier space, case-insensitive identifiers, a disabled
///     client being returned as disabled, and a reload on the client change signals. It lives
///     with the filtering tests because the filter engine is the directory's first user.
/// </summary>
public class DnsClientDirectoryTests
{
    private readonly InMemoryDnsClientRepository _clients = new();
    private readonly InMemoryDnsClientAddressRepository _addresses = new();
    private readonly EntityChangeNotifier _notifier = new();
    private readonly DnsClientDirectory _directory;

    public DnsClientDirectoryTests()
    {
        ServiceCollection services = new();
        services.AddSingleton<IDnsClientRepository>(_clients);
        services.AddSingleton<IDnsClientAddressRepository>(_addresses);
        _directory = new DnsClientDirectory(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _notifier,
            NullLogger<DnsClientDirectory>.Instance
        );
    }

    private DnsClientEntity AddClient(
        string identifier,
        InternetProtocolVersionEnum version = InternetProtocolVersionEnum.IPv4,
        Guid? groupId = null,
        bool isDisabled = false
    )
    {
        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            ClientGroupId = groupId,
            IsDisabled = isDisabled,
        };
        _clients.Items.Add(client);
        _addresses.Items.Add(
            new DnsClientAddressEntity
            {
                Id = Guid.NewGuid(),
                ClientId = client.Id,
                ClientIdentifier = identifier,
                ProtocolVersion = version,
            }
        );

        return client;
    }

    [Fact]
    public async Task FindAsync_ForAKnownIdentifier_ReturnsTheClientIgnoringCase()
    {
        Guid groupId = Guid.NewGuid();
        DnsClientEntity client = AddClient("AA:BB:CC:DD:EE:FF", groupId: groupId);

        DnsClientInfo found = await _directory.FindAsync(
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );

        Assert.NotNull(found);
        Assert.Equal(client.Id, found.ClientId);
        Assert.Equal(groupId, found.ClientGroupId);
        Assert.False(found.IsDisabled);
    }

    [Fact]
    public async Task FindAsync_InTheOtherIdentifierSpace_ReturnsNull()
    {
        AddClient("aa:bb:cc:dd:ee:ff");

        Assert.Null(
            await _directory.FindAsync("aa:bb:cc:dd:ee:ff", InternetProtocolVersionEnum.IPv6)
        );
        Assert.Null(await _directory.FindAsync(null, InternetProtocolVersionEnum.IPv4));
    }

    [Fact]
    public async Task FindAsync_ForADisabledClient_ReturnsItMarkedDisabled()
    {
        AddClient("aa:bb:cc:dd:ee:ff", isDisabled: true);

        DnsClientInfo found = await _directory.FindAsync(
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );

        Assert.True(found.IsDisabled);
    }

    [Fact]
    public async Task FindAsync_AfterAClientIsAdded_SeesItOnlyOnceTheClientSignalFires()
    {
        Assert.Null(await _directory.FindAsync("11:11", InternetProtocolVersionEnum.IPv4));

        AddClient("11:11");

        Assert.Null(await _directory.FindAsync("11:11", InternetProtocolVersionEnum.IPv4));

        _notifier.NotifyChanged([typeof(DnsClientAddressEntity)]);

        Assert.NotNull(await _directory.FindAsync("11:11", InternetProtocolVersionEnum.IPv4));
    }

    [Fact]
    public async Task FindAsync_AfterTheDirectoryIsDisposed_IgnoresFurtherSignals()
    {
        Assert.Null(await _directory.FindAsync("22:22", InternetProtocolVersionEnum.IPv4));
        AddClient("22:22");
        _directory.Dispose();

        _notifier.NotifyChanged([typeof(DnsClientEntity)]);

        Assert.Null(await _directory.FindAsync("22:22", InternetProtocolVersionEnum.IPv4));
    }
}
