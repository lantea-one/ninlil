using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.QueryLog;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="DnsQueryLogEntryDetail" />,
///     as returned by <see cref="Controllers.QueryLogController.Search" />.
/// </summary>
public class DnsQueryLogEntryDetailPagedResultExample
    : IExamplesProvider<PagedResult<DnsQueryLogEntryDetail>>
{
    /// <summary>
    ///     This method returns two entries from one client: an answered A query, with the client's
    ///     resolved identifier, and an NXDOMAIN for a name the server does not host, with no
    ///     identifier.
    /// </summary>
    public PagedResult<DnsQueryLogEntryDetail> GetExamples() =>
        new(
            [
                new DnsQueryLogEntryDetail(
                    Guid.Parse("1a2b3c4d-5e6f-4708-9a0b-1c2d3e4f5061"),
                    DateTimeOffset.Parse("2026-08-31T14:03:12.481+00:00"),
                    "198.51.100.23",
                    DnsTransportProtocolEnum.Udp,
                    "printer.internal.example.com",
                    DnsResourceRecordTypeEnum.A,
                    DnsResponseCodeEnum.NoError,
                    1,
                    "AA:BB:CC:DD:EE:FF",
                    InternetProtocolVersionEnum.IPv4
                ),
                new DnsQueryLogEntryDetail(
                    Guid.Parse("2b3c4d5e-6f70-4819-ab0c-2d3e4f506172"),
                    DateTimeOffset.Parse("2026-08-31T14:03:09.117+00:00"),
                    "198.51.100.23",
                    DnsTransportProtocolEnum.Udp,
                    "this-host-does-not-exist.example.com",
                    DnsResourceRecordTypeEnum.A,
                    DnsResponseCodeEnum.NxDomain,
                    0,
                    null,
                    null
                ),
            ],
            0,
            50,
            2
        );
}
