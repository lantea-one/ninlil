// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This exception is thrown when an administrative operation is individually well-formed but
///     conflicts with the current state of the data -- a duplicate name, an attempt to add a
///     second SOA record to a zone that already has one, deleting an SOA record directly instead
///     of deleting its owning zone, writing a manual entry into a subscribed filter list whose own
///     entries are wholesale-replaced on a refresh schedule, and so on. Mapped by the REST API's
///     own shared exception handler to a <c>409</c> response.
/// </summary>
/// <param name="message">
///     A description of the conflict, safe to return to the caller as-is -- this project's own
///     established convention is to name the specific rule that was violated, not just say
///     "conflict."
/// </param>
public sealed class ConflictException(string message) : NinlilException(message);
