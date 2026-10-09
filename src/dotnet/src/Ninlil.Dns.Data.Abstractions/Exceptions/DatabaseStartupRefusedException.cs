// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Exceptions;

/// <summary>
///     This exception reports that startup must stop because the database is in a state this
///     version of Ninlil must not run on: it was upgraded by a newer version, or a migration
///     failed. The message is written for the operator and says what happened, what state the
///     database is in and what to do next. <c>Program</c> catches it, logs the message at
///     Critical and exits with code 78 (EX_CONFIG), which the systemd units list in
///     <c>RestartPreventExitStatus</c> so the supervisor does not restart in a loop. The relational
///     startup migrator throws it today; the MongoDB schema guard throws it too.
/// </summary>
public class DatabaseStartupRefusedException : Exception
{
    /// <summary>
    ///     This constant contains the process exit code used when startup is refused
    ///     (EX_CONFIG from <c>sysexits.h</c>).
    /// </summary>
    public const int ExitCode = 78;

    /// <summary>
    ///     This constructor creates the exception with an operator-facing message.
    /// </summary>
    /// <param name="message">What happened and what to do next.</param>
    public DatabaseStartupRefusedException(string message)
        : base(message) { }

    /// <summary>
    ///     This constructor creates the exception with an operator-facing message and the
    ///     failure that caused it.
    /// </summary>
    /// <param name="message">What happened and what to do next.</param>
    /// <param name="innerException">The failure that caused the refusal.</param>
    public DatabaseStartupRefusedException(string message, Exception innerException)
        : base(message, innerException) { }
}
