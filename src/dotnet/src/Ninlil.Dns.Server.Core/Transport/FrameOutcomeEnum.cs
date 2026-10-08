// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This enum is what a frame handler tells <see cref="FramedDnsStreamSession" /> after it
///     has dealt with one received message. The session uses it to decide who releases the
///     message's in-flight slot and whether to keep reading.
/// </summary>
public enum FrameOutcomeEnum
{
    /// <summary>
    ///     The handler finished with the message, with or without writing a reply directly. The
    ///     session releases the slot and reads the next message.
    /// </summary>
    Handled,

    /// <summary>
    ///     The handler enqueued the message onto the pipeline with the callbacks from
    ///     <see cref="FramedDnsStreamSession.CreateResponder" /> and
    ///     <see cref="FramedDnsStreamSession.CreateDropNotifier" />. One of them releases the slot.
    ///     The session reads the next message.
    /// </summary>
    Enqueued,

    /// <summary>
    ///     The handler finished with the message and the connection must close once every
    ///     reply is written (a zone transfer, which the pull client reads to the end of the
    ///     stream). The session releases the slot, stops reading, flushes and returns.
    /// </summary>
    CloseAfterDrain,
}
