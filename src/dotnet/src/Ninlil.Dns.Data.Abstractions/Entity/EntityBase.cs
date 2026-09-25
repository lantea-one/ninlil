// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class is the base of every persisted Ninlil entity. It carries the two things every
///     stored record needs whatever it represents and whichever engine stores it: a unique
///     identifier and the time it was first written. <see cref="AuditableEntityBase" /> adds
///     update tracking on top.
/// </summary>
public abstract class EntityBase
{
    /// <summary>
    ///     This property contains the entity's unique identifier. The code that creates the entity
    ///     generates it, not the database, so identifiers work the same in relational databases and
    ///     MongoDB.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time at which the entity was first written to
    ///     storage.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }
}
