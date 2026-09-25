// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This exception is thrown when an administrative operation is well-formed but conflicts with
///     the current data: a duplicate name, a second SOA record in a zone, deleting an SOA record
///     instead of its zone, a manual entry in a subscribed filter list, and so on. The REST API's
///     shared exception handler maps it to a <c>409</c> response.
/// </summary>
/// <param name="message">
///     A description of the conflict, safe to return to the caller. By convention it names the
///     rule that was broken rather than just saying "conflict".
/// </param>
public sealed class ConflictException(string message) : NinlilException(message);
