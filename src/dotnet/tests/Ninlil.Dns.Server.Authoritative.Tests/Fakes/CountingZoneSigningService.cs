using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Fakes;

/// <summary>
///     This class is an <see cref="IZoneSigningService" /> that signs nothing: it counts its calls
///     and returns an empty <see cref="SignedZoneData" />, so a test can tell a zone that was signed
///     again from one that was left alone.
/// </summary>
public sealed class CountingZoneSigningService : IZoneSigningService
{
    private int _callCount;

    /// <summary>
    ///     This property is the number of <see cref="SignZoneAsync" /> calls so far.
    /// </summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>
    ///     This property is the zone of the latest call.
    /// </summary>
    public Guid LastZoneId { get; private set; }

    /// <inheritdoc />
    public Task<SignedZoneData> SignZoneAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        LastZoneId = zoneId;
        Interlocked.Increment(ref _callCount);

        return Task.FromResult(
            new SignedZoneData(
                [],
                new Dictionary<
                    (DnsName, DnsResourceRecordTypeEnum),
                    IReadOnlyList<DnsResourceRecord>
                >(),
                [],
                null,
                new Dictionary<DnsName, IReadOnlyList<DnsResourceRecord>>(),
                DateTimeOffset.UtcNow.AddDays(7)
            )
        );
    }
}
