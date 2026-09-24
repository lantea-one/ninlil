using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt;

/// <summary>
///     This class answers the one kind of query DNSCrypt clients send in the clear, never
///     encrypted: a TXT query for <c>&lt;protocol-major-version&gt;.dnscrypt-cert.&lt;provider-
///     name&gt;</c>, by which a client discovers a resolver's currently valid certificate(s) --
///     its public key(s), which cipher(s) to use, and which client-magic to tag its own
///     encrypted queries with. There is no zone data behind this name anywhere in this project
///     (it is not a real, hosted zone record), so this listener answers it directly rather than
///     routing it through <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />.
/// </summary>
internal static class DnsCryptCertificateDiscoveryResponder
{
    /// <summary>
    ///     This constant contains the DNSCrypt protocol's own fixed major version number, used
    ///     literally in the certificate discovery name.
    /// </summary>
    private const string ProtocolMajorVersion = "2";

    /// <summary>
    ///     This constant contains how long a client may cache a certificate discovery response
    ///     for. Deliberately short -- far shorter than
    ///     <see cref="DnsCryptOptions.CertificateValidityDuration" /> -- so a client notices a
    ///     newly rotated-in certificate reasonably promptly rather than continuing to use a
    ///     certificate this resolver may have already stopped tracking.
    /// </summary>
    private const uint DiscoveryTimeToLiveSeconds = 60;

    /// <summary>
    ///     This method builds the certificate discovery name this resolver answers under, for a
    ///     given configured provider name.
    /// </summary>
    public static DnsName BuildProviderCertificateName(string providerName) =>
        DnsName.Parse($"{ProtocolMajorVersion}.dnscrypt-cert.{providerName}");

    /// <summary>
    ///     This method checks whether a decoded, plaintext query is a certificate discovery
    ///     request for this resolver's own provider name, and if so, builds the response.
    /// </summary>
    /// <param name="request">A successfully decoded, plaintext query.</param>
    /// <param name="providerCertificateName">This resolver's own certificate discovery name, from <see cref="BuildProviderCertificateName" />.</param>
    /// <param name="store">The store every currently tracked certificate is read from.</param>
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

            // A single RFC 1035 §3.3 <character-string>: a one-byte length prefix (the
            // certificate is always exactly 124 bytes, well under the 255-byte limit) followed
            // by the certificate's own raw bytes -- built by hand, rather than through
            // TxtRecordDataCodec, because that codec round-trips its value through UTF-8 text,
            // which would corrupt a certificate's arbitrary binary content.
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
