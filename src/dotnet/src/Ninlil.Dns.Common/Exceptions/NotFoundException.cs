// Define our namespace.
namespace Ninlil.Dns.Common.Exceptions;

/// <summary>
///     This exception is thrown when an administrative operation looks up an entity by its own
///     identifier (or another unique key, such as a name) and finds nothing -- a zone, a record,
///     a TSIG key, a user, a role, and so on. Mapped by the REST API's own shared exception
///     handler to a <c>404</c> response; carries the entity's own type name and the key that
///     failed to resolve so that response can name both without every call site re-deriving the
///     same detail independently.
/// </summary>
/// <param name="entityType">
///     The kind of entity that could not be found -- a short, human-readable name such as
///     <c>"Zone"</c> or <c>"User"</c>, not a fully-qualified .NET type name.
/// </param>
/// <param name="entityId">
///     The identifier or key that was looked up and found nothing -- typically a
///     <see cref="Guid" />'s own string form or a name, rendered exactly as a caller would
///     recognize it.
/// </param>
public sealed class NotFoundException(string entityType, string entityId)
    : NinlilException($"{entityType} '{entityId}' could not be found.")
{
    /// <summary>
    ///     The kind of entity that could not be found, exactly as passed to the constructor.
    /// </summary>
    public string EntityType { get; } = entityType;

    /// <summary>
    ///     The identifier or key that was looked up and found nothing, exactly as passed to the
    ///     constructor.
    /// </summary>
    public string EntityId { get; } = entityId;
}
