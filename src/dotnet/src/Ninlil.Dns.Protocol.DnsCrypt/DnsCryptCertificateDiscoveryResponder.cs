using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt;

/// <summary>
///     This class answers the one query DNSCrypt clients send unencrypted: a TXT query for
///     <c>&lt;protocol-major-version&gt;.dnscrypt-cert.&lt;provider-name&gt;</c>, through which a
///     client finds the resolver's valid certificates (public keys, ciphers, and the client-magic
///     to tag its encrypted queries with). No zone holds this name, so
///     <see cref="DnsCryptQueryHandler" /> answers it here rather than sending it through
///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />. The handler
///     applies the rate limits and, over UDP, the size ceiling to the answer built here; the
///     pipeline's other checks (access control, filtering) do not run for discovery.
/// </summary>
internal static class DnsCryptCertificateDiscoveryResponder
{
    /// <summary>
    ///     This constant contains the DNSCrypt protocol major version, used in the certificate
    ///     discovery name.
    /// </summary>
    private const string ProtocolMajorVersion = "2";

    /// <summary>
    ///     This constant contains how long a client may cache a certificate discovery response.
    ///     It is much shorter than <see cref="DnsCryptOptions.CertificateValidityDuration" />, so a
    ///     client notices a newly rotated certificate soon rather than keep using one the resolver
    ///     may no longer track.
    /// </summary>
    private const uint DiscoveryTimeToLiveSeconds = 60;

    /// <summary>
    ///     This method builds the certificate discovery name for a configured provider name.
    /// </summary>
    public static DnsName BuildProviderCertificateName(string providerName) =>
        DnsName.Parse($"{ProtocolMajorVersion}.dnscrypt-cert.{providerName}");

    /// <summary>
    ///     This method checks whether a decoded plaintext query is a certificate discovery request
    ///     for this resolver's provider name and, if so, builds the response: one TXT record per
    ///     certificate valid now. Expired and not-yet-valid certificates are left out.
    /// </summary>
    /// <param name="request">A successfully decoded, plaintext query.</param>
    /// <param name="providerCertificateName">The certificate discovery name, from <see cref="BuildProviderCertificateName" />.</param>
    /// <param name="store">The store the tracked certificates are read from.</param>
    /// <param name="response">The encoded response, if this was a matching request.</param>
    /// <returns>A <see langword="bool" /> indicating whether <paramref name="request" /> matched.</returns>
    public static bool TryBuildResponse(
        DnsMessage request,
        DnsName providerCertificateName,
        IDnsCryptCertificateStore store,
        out DnsMessage response
    )
    {
        if (
            request.Questions.Count != 1
            || request.Questions[0].Type != DnsResourceRecordTypeEnum.Txt
            || request.Questions[0].Name != providerCertificateName
        )
        {
            response = null;
            return false;
        }

        DnsQuestion question = request.Questions[0];
        DateTimeOffset now = DateTimeOffset.UtcNow;

        response = new DnsMessage
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Query,
            IsAuthoritativeAnswer = false,
            RecursionAvailable = false,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };
        response.Questions.Add(question);

        foreach (DnsCryptCertificateEntry entry in store.Entries)
        {
            if (entry.Certificate.ValidFrom > now || entry.Certificate.ValidUntil <= now)
                continue;

            byte[] encodedCertificate = DnsCryptCertificateCodec.Encode(entry.Certificate);

            // One RFC 1035 §3.3 <character-string>: a one-byte length (a certificate is always
            // 124 bytes, under the 255-byte limit) and the certificate bytes. It is built by hand
            // rather than with TxtRecordDataCodec, which goes through UTF-8 text and would
            // corrupt the binary certificate.
            byte[] rdata = new byte[1 + encodedCertificate.Length];
            rdata[0] = (byte)encodedCertificate.Length;
            encodedCertificate.CopyTo(rdata, 1);

            response.Answers.Add(
                new DnsResourceRecord(
                    question.Name,
                    DnsResourceRecordTypeEnum.Txt,
                    DnsClassEnum.Internet,
                    DiscoveryTimeToLiveSeconds,
                    rdata
                )
            );
        }

        return true;
    }
}
