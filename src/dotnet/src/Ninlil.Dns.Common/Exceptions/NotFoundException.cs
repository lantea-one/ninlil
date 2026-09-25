// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This exception is thrown when an administrative operation looks up an entity (a zone, a
///     record, a TSIG key, a user, a role, and so on) by identifier or another unique key and finds
///     nothing. The REST API's shared exception handler maps it to a <c>404</c> response. It
///     carries the entity type and the key so the response can name both.
/// </summary>
/// <param name="entityType">
///     The kind of entity that could not be found: a short, readable name such as <c>"Zone"</c> or
///     <c>"User"</c>, not a .NET type name.
/// </param>
/// <param name="entityId">
///     The identifier or key that was looked up, usually a <see cref="Guid" /> string or a name,
///     in the form the caller would recognize.
/// </param>
public sealed class NotFoundException(string entityType, string entityId)
    : NinlilException($"{entityType} '{entityId}' could not be found.")
{
    /// <summary>
    ///     The kind of entity that could not be found, as passed to the constructor.
    /// </summary>
    public string EntityType { get; } = entityType;

    /// <summary>
    ///     The identifier or key that was looked up, as passed to the constructor.
    /// </summary>
    public string EntityId { get; } = entityId;
}
