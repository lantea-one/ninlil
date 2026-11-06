using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Exceptions;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This interface writes one error entry to the Windows Application event log. It exists so
///     <see cref="StartupRefusalReporter" /> can be tested on any operating system.
/// </summary>
public interface IStartupRefusalEventLogWriter
{
    /// <summary>
    ///     This method writes <paramref name="message" /> as an error under
    ///     <paramref name="source" />.
    /// </summary>
    /// <param name="source">The event source name.</param>
    /// <param name="message">The entry text.</param>
    void WriteError(string source, string message);
}

/// <summary>
///     This class writes to the Windows Application event log through
///     <see cref="EventLog" />. It is only used when the process runs as a Windows service.
/// </summary>
public sealed class WindowsStartupRefusalEventLogWriter : IStartupRefusalEventLogWriter
{
    // The event log rejects an entry longer than 32766 characters.
    private const int MaximumMessageLength = 32000;

    /// <inheritdoc />
    public void WriteError(string source, string message)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The Windows event log needs Windows.");
        if (message.Length > MaximumMessageLength)
            message = message[..MaximumMessageLength];
        // The instance form does not look the source up in every log first, which a service
        // account without read access to the Security log cannot do.
        using EventLog log = new("Application") { Source = source };
        log.WriteEntry(message, EventLogEntryType.Error);
    }
}

/// <summary>
///     This class reports a deliberate startup refusal (a
///     <see cref="DatabaseStartupRefusedException" />) from every refusal handler in
///     <c>Program.cs</c>. It logs the message as critical, which reaches the console of a
///     console run, and, when the process is a Windows service, also writes it as an error to the
///     Application event log under <see cref="EventLogSource" />, the source the README's
///     Windows install steps register. A service has no console, so without the event entry an
///     operator could not tell why the service stopped.
///     <para>
///         Every refusal happens before the host starts, so the process has not yet connected to
///         the Service Control Manager. The process exit code 78 is set by the caller; this class
///         only reports.
///     </para>
/// </summary>
public static class StartupRefusalReporter
{
    /// <summary>
    ///     This constant contains the event source, the one the Windows install steps register.
    /// </summary>
    public const string EventLogSource = "Ninlil.Web";

    /// <summary>
    ///     This method reports a refusal with the real service check and the Windows event log.
    ///     It never throws.
    /// </summary>
    /// <param name="logger">
    ///     The logger to write the critical message to, or <c>null</c> to log to a console logger
    ///     created for the call.
    /// </param>
    /// <param name="category">The logger category used when <paramref name="logger" /> is null.</param>
    /// <param name="refusal">The refusal; its message is the text reported.</param>
    public static void Report(
        ILogger logger,
        string category,
        DatabaseStartupRefusedException refusal
    ) =>
        Report(
            logger,
            category,
            refusal,
            OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService(),
            new WindowsStartupRefusalEventLogWriter()
        );

    /// <summary>
    ///     This method reports a refusal with an explicit service flag and event log writer.
    ///     It never throws.
    /// </summary>
    /// <param name="logger">The logger, or <c>null</c> to log to a console logger created for the call.</param>
    /// <param name="category">The logger category used when <paramref name="logger" /> is null.</param>
    /// <param name="refusal">The refusal; its message is the text reported.</param>
    /// <param name="isWindowsService">Whether the process runs as a Windows service.</param>
    /// <param name="eventLogWriter">The event log writer, called only for a service.</param>
    public static void Report(
        ILogger logger,
        string category,
        DatabaseStartupRefusedException refusal,
        bool isWindowsService,
        IStartupRefusalEventLogWriter eventLogWriter
    )
    {
        ILoggerFactory ownFactory = null;
        try
        {
            if (logger == null)
            {
                ownFactory = LoggerFactory.Create(logging => logging.AddConsole());
                logger = ownFactory.CreateLogger(category);
            }
            logger.LogCritical(
                refusal.InnerException,
                "Startup refused: {Message}",
                refusal.Message
            );

            if (!isWindowsService)
                return;
            try
            {
                eventLogWriter.WriteError(EventLogSource, "Startup refused: " + refusal.Message);
            }
            catch (Exception exception)
            {
                // The source is not registered, or the account cannot write the log. The
                // refusal itself still ends the start with exit code 78, so say why the entry
                // is missing and carry on.
                logger.LogWarning(
                    "The refusal could not be written to the Application event log: {Reason}",
                    exception.Message
                );
            }
        }
        catch (Exception)
        {
            // Reporting must never replace the refusal with another failure.
        }
        finally
        {
            ownFactory?.Dispose();
        }
    }
}
