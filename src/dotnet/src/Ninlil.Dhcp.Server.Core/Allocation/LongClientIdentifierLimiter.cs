using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This class decides whether a client identifier is too long to serve and logs each refusal
///     without flooding the log. <see cref="Dhcpv4RequestHandler" /> and
///     <see cref="Dhcpv6RequestHandler" /> each own one and call <see cref="IsTooLong" /> before
///     they allocate or save anything.
///     <para>
///         The limit is <see cref="ClientIdentifierFormat.MaximumBytes" />, the same cap the web
///         API applies to reservation, client and static-mapping identifiers. It also matches the
///         size of the stored <c>ClientIdentifier</c> columns and of the composite unique indexes
///         over them, which stay within the SQL Server and MySQL index key limits only at that
///         size. A longer identifier would fail the save, so the request is dropped instead.
///     </para>
///     <para>
///         A client can resend a long identifier many times a second, so <see cref="Refuse" />
///         logs one Warning per <see cref="WarningInterval" /> that carries the number of
///         refusals since the previous Warning, and logs the others at Debug.
///     </para>
/// </summary>
/// <param name="timeProvider">The clock that decides when the next Warning is due.</param>
internal sealed class LongClientIdentifierLimiter(TimeProvider timeProvider)
{
    /// <summary>
    ///     This constant is the shortest time between two Warnings.
    /// </summary>
    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    // Guards the three fields below; requests arrive on many threads.
    private readonly object _lock = new();
    private DateTimeOffset? _lastWarningUtc;
    private int _refusedSinceWarning;

    /// <summary>
    ///     This method reports whether an identifier is longer than the limit.
    /// </summary>
    /// <param name="identifier">The identifier bytes, or <see langword="null" /> for none.</param>
    /// <returns><see langword="true" /> if the request must be refused.</returns>
    public static bool IsTooLong(byte[] identifier) =>
        identifier is not null && identifier.Length > ClientIdentifierFormat.MaximumBytes;

    /// <summary>
    ///     This method records one refusal: a Warning when one is due, a Debug entry otherwise.
    /// </summary>
    /// <param name="logger">The calling handler's logger.</param>
    /// <param name="protocol">The protocol name for the message, such as <c>DHCPv4</c>.</param>
    /// <param name="remoteEndPoint">The sender of the refused request.</param>
    /// <param name="identifierLength">The length of the refused identifier in bytes.</param>
    public void Refuse(
        ILogger logger,
        string protocol,
        IPEndPoint remoteEndPoint,
        int identifierLength
    )
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        int refusedCount = 0;
        bool warn;

        lock (_lock)
        {
            _refusedSinceWarning++;
            warn = _lastWarningUtc is null || now - _lastWarningUtc.Value >= WarningInterval;

            if (warn)
            {
                refusedCount = _refusedSinceWarning;
                _refusedSinceWarning = 0;
                _lastWarningUtc = now;
            }
        }

        if (warn)
        {
            logger.LogWarning(
                "Refused {RefusedCount} {Protocol} request(s) since the last warning because the client identifier is longer than {MaximumBytes} bytes; the latest was {IdentifierLength} bytes from {RemoteEndPoint}. Further refusals are logged at Debug level.",
                refusedCount,
                protocol,
                ClientIdentifierFormat.MaximumBytes,
                identifierLength,
                remoteEndPoint
            );
        }
        else
        {
            logger.LogDebug(
                "Refused a {Protocol} request from {RemoteEndPoint}: its client identifier is {IdentifierLength} bytes, over the {MaximumBytes}-byte limit.",
                protocol,
                remoteEndPoint,
                identifierLength,
                ClientIdentifierFormat.MaximumBytes
            );
        }
    }
}
