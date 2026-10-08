// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration names the three shapes a TSIG digest input can take (RFC 8945 §4.3 and
///     §5.3.1). <see cref="Ninlil.Dns.Common.Wire.TsigCodec.BuildDigestInput" /> assembles the bytes
///     for each, and the TSIG service uses the same call to sign and to verify.
/// </summary>
public enum TsigDigestKindEnum
{
    /// <summary>
    ///     A request, or any other message that does not answer a signed message: the message bytes
    ///     followed by the full TSIG variables.
    /// </summary>
    Request = 0,

    /// <summary>
    ///     A single response, or the first message of a multi-message answer: the request MAC (with
    ///     its length), the message bytes and the full TSIG variables.
    /// </summary>
    FirstResponse = 1,

    /// <summary>
    ///     A later signed message of a multi-message answer: the prior MAC (with its length), the
    ///     bytes of every unsigned message since it, this message's bytes and only the TSIG timers.
    /// </summary>
    Continuation = 2,
}
