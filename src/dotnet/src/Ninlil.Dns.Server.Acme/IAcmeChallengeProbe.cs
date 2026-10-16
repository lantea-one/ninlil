using System.Net;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This interface asks one name server whether it serves a DNS-01 challenge value.
///     <see cref="AcmeChallengePropagationWaiter" /> calls it for every server that may answer for
///     the zone; <see cref="UdpAcmeChallengeProbe" /> is the default, and tests replace it with a
///     fake.
/// </summary>
public interface IAcmeChallengeProbe
{
    /// <summary>
    ///     This method sends one TXT query for <paramref name="name" /> to <paramref name="server" />
    ///     and reports whether the answer holds <paramref name="dnsTxtValue" />.
    /// </summary>
    /// <param name="server">The address of the name server to ask.</param>
    /// <param name="name">The challenge name, <c>_acme-challenge.&lt;domain&gt;</c>.</param>
    /// <param name="dnsTxtValue">The key authorization digest the CA will look for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> when the server answered with the value, otherwise
    ///     <see langword="false" /> (no answer, another answer or a timeout).
    /// </returns>
    Task<bool> ServesValueAsync(
        IPAddress server,
        DnsName name,
        string dnsTxtValue,
        CancellationToken stoppingToken
    );
}
