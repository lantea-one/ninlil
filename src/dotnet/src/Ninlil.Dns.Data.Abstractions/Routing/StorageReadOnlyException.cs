// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This exception reports an attempt to write through a read-only storage view: a scope whose
///     <see cref="StorageReadIntent" /> resolved to a replica. It is a programming error, never a
///     condition to handle: a write belongs in a scope that reads from the primary. The storage
///     engines throw it so the mistake fails in a test instead of writing to the wrong server or
///     failing with an engine-specific error.
/// </summary>
/// <param name="message">What was attempted.</param>
public sealed class StorageReadOnlyException(string message) : InvalidOperationException(message)
{
    /// <summary>
    ///     This method builds the exception for a write attempt.
    /// </summary>
    /// <param name="attempt">A short description of the write, for example "SaveChanges".</param>
    /// <returns>The exception to throw.</returns>
    public static StorageReadOnlyException For(string attempt) =>
        new(
            $"{attempt} was attempted in a scope that reads from a replica. Writes must run in a "
                + "scope with the primary read intent (the default); do not set the replica read "
                + "intent on work that writes."
        );
}
