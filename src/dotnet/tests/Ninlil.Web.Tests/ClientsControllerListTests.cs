using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how <see cref="ClientsController.List" /> loads the addresses of a page.
///     The controller is built directly over hand-written repository stand-ins, with no host. The
///     address stand-in counts its calls and fails on any lookup other than the one-query lookup
///     for the page, so the test shows that a page costs one address query however many clients
///     it holds.
/// </summary>
public class ClientsControllerListTests
{
    /// <summary>
    ///     This proxy answers only the calls the test configures and throws for every other
    ///     member, so a test need not implement a whole repository interface.
    /// </summary>
    public class StubProxy : DispatchProxy
    {
        /// <summary>Gets or sets the handler that answers each call by method name.</summary>
        public Func<string, object[], object> Handler { get; set; }

        /// <inheritdoc />
        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            Handler(targetMethod.Name, args);
    }

    private static T Stub<T>(Func<string, object[], object> handler)
        where T : class
    {
        T proxy = DispatchProxy.Create<T, StubProxy>();
        ((StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    /// <summary>
    ///     This test checks that a page of three clients runs one address query for all of them,
    ///     that each client gets its own addresses, and that the page keeps the order of the
    ///     clients returned by storage.
    /// </summary>
    [Fact]
    public async Task List_LoadsThePagesAddressesWithOneQuery_AndKeepsClientOrder()
    {
        DnsClientEntity[] clients =
        [
            .. Enumerable
                .Range(0, 3)
                .Select(index => new DnsClientEntity
                {
                    Id = Guid.NewGuid(),
                    DisplayName = $"client-{index}",
                }),
        ];
        // The middle client has no addresses and the last has two; the rows come back in
        // a different order from the clients, as one query for several clients may return them.
        DnsClientAddressEntity[] addresses =
        [
            Address(clients[2].Id, "aa:00:00:00:00:03"),
            Address(clients[0].Id, "aa:00:00:00:00:01"),
            Address(clients[2].Id, "aa:00:00:00:00:04"),
        ];
        int lookups = 0;
        List<Guid> askedIds = [];

        IDnsClientRepository clientRepository = Stub<IDnsClientRepository>(
            (name, _) =>
                name switch
                {
                    "SearchAsync" => Task.FromResult<IReadOnlyList<DnsClientEntity>>(clients),
                    "CountAsync" => Task.FromResult(clients.Length),
                    _ => throw new NotSupportedException(name),
                }
        );
        IDnsClientAddressRepository addressRepository = Stub<IDnsClientAddressRepository>(
            (name, arguments) =>
            {
                if (name != "ListByClientIdsAsync")
                    throw new NotSupportedException(name);

                Interlocked.Increment(ref lookups);
                askedIds.AddRange((IReadOnlyCollection<Guid>)arguments[0]);
                return Task.FromResult<IReadOnlyList<DnsClientAddressEntity>>(addresses);
            }
        );

        ClientsController controller = new(
            clientRepository,
            addressRepository,
            null,
            null,
            null,
            null,
            null,
            new StaticOptionsMonitor<ClientPresenceOptions>(new ClientPresenceOptions()),
            Stub<IMacVendorLookupService>((_, _) => MacVendorLookupResult.NoMacAddress)
        );

        ActionResult<PagedResult<ClientDetail>> result = await controller.List(
            new PaginationQuery(),
            null,
            null,
            null,
            null,
            CancellationToken.None
        );

        PagedResult<ClientDetail> page = Assert.IsType<PagedResult<ClientDetail>>(
            Assert.IsType<OkObjectResult>(result.Result).Value
        );
        Assert.Equal(1, lookups);
        Assert.Equal(clients.Select(client => client.Id), askedIds);
        Assert.Equal(clients.Select(client => client.Id), page.Items.Select(detail => detail.Id));
        Assert.Equal([1, 0, 2], page.Items.Select(detail => detail.Addresses.Count));
        Assert.Equal(
            ["aa:00:00:00:00:03", "aa:00:00:00:00:04"],
            page.Items[2].Addresses.Select(address => address.ClientIdentifier)
        );
    }

    private static DnsClientAddressEntity Address(Guid clientId, string identifier) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ClientIdentifier = identifier,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            Address = "192.0.2.1",
        };
}
