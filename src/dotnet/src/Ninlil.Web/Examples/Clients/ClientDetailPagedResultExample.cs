using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Clients;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="ClientDetail" />, referenced by <see cref="Controllers.ClientsController.List" />.
/// </summary>
public class ClientDetailPagedResultExample : IExamplesProvider<PagedResult<ClientDetail>>
{
    /// <summary>
    ///     This method returns one page of the same two clients <see cref="ClientDetailExample" />
    ///     already shows individually, so a caller reading this example sees both shapes together,
    ///     in the exact envelope a real list response carries.
    /// </summary>
    public PagedResult<ClientDetail> GetExamples() =>
        new(
            [
                new ClientDetail(
                    Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
                    [
                        new ClientAddressDetail(
                            Guid.Parse("f1a2b3c4-1234-4abc-9def-0123456789ab"),
                            Guid.Parse("d4e5f6a1-8192-4ab1-c3d4-e5f6a1b2c3d3"),
                            "b8:27:eb:12:34:56",
                            InternetProtocolVersionEnum.IPv4,
                            "192.0.2.42",
                            DateTimeOffset.Parse("2026-09-10T09:00:00+00:00"),
                            DateTimeOffset.Parse("2026-09-18T08:00:00+00:00"),
                            DateTimeOffset.Parse("2026-09-10T09:00:00+00:00"),
                            "b8:27:eb:12:34:56",
                            "Raspberry Pi Foundation",
                            false
                        ),
                    ],
                    "Living Room TV",
                    Guid.Parse("c3d4e5f6-7081-49a0-b2c3-d4e5f6a1b2c2"),
                    false,
                    "Confirmed via its MAC address label.",
                    DnsClientOriginEnum.Manual,
                    DateTimeOffset.Parse("2026-09-10T09:00:00+00:00"),
                    null,
                    null,
                    false,
                    DateTimeOffset.Parse("2026-09-18T08:00:00+00:00"),
                    true,
                    DnsClientDeviceIconEnum.Television,
                    DnsClientDeviceIconEnum.SingleBoardComputer,
                    true,
                    "Raspberry Pi Foundation",
                    false
                ),
                new ClientDetail(
                    Guid.Parse("e5f6a1b2-92a3-4bc2-d4e5-f6a1b2c3d4e4"),
                    [
                        new ClientAddressDetail(
                            Guid.Parse("a2b3c4d5-2345-4bcd-8ef0-123456789abc"),
                            Guid.Parse("e5f6a1b2-92a3-4bc2-d4e5-f6a1b2c3d4e4"),
                            "00:03:00:01:aa:bb:cc:dd:ee:ff",
                            InternetProtocolVersionEnum.IPv6,
                            null,
                            DateTimeOffset.Parse("2026-09-15T21:14:00+00:00"),
                            null,
                            DateTimeOffset.Parse("2026-09-15T21:14:00+00:00"),
                            "aa:bb:cc:dd:ee:ff",
                            null,
                            true
                        ),
                    ],
                    null,
                    null,
                    false,
                    null,
                    DnsClientOriginEnum.Dhcp,
                    DateTimeOffset.Parse("2026-09-15T21:14:00+00:00"),
                    null,
                    DateTimeOffset.Parse("2026-09-17T22:00:00+00:00"),
                    true,
                    null,
                    false,
                    null,
                    null,
                    false,
                    null,
                    true
                ),
            ],
            0,
            50,
            2
        );
}
