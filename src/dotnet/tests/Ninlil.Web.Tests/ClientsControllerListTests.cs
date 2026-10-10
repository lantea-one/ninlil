using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how <see cref="ClientsController.List" /> loads each client's addresses.
///     The controller is built directly over hand-written repository stand-ins, with no host. The
///     address stand-in fails when a second lookup starts before the first one finished, which is
///     what a single scoped DbContext does when the lookups run in parallel.
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
    ///     This test checks that the address lookups run one after another and that the page keeps
    ///     the order of the clients returned by storage.
    /// </summary>
    [Fact]
    public async Task List_LoadsAddressesOneClientAtATime_AndKeepsClientOrder()
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
        int running = 0;

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
            (name, _) =>
            {
                if (name != "ListByClientIdAsync")
                    throw new NotSupportedException(name);

                return Lookup();

                async Task<IReadOnlyList<DnsClientAddressEntity>> Lookup()
                {
                    if (Interlocked.Increment(ref running) > 1)
                        throw new InvalidOperationException("Two lookups ran at the same time.");
                    await Task.Delay(20);
                    Interlocked.Decrement(ref running);
                    return [];
                }
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
            null
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
        Assert.Equal(clients.Select(client => client.Id), page.Items.Select(detail => detail.Id));
    }
}
