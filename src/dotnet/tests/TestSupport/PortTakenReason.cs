using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.TestSupport;

/// <summary>
///     Tells whether a log line or health text carries the operating system's reason for a bind
///     that failed because another socket holds the port. The file is linked into every test
///     project by <c>src/dotnet/tests/Directory.Build.props</c>.
/// </summary>
/// <remarks>
///     The product passes the <see cref="SocketException" /> message through, so the wording is
///     the OS's. A second bind to a taken port fails with <see cref="SocketError.AddressAlreadyInUse" />
///     on Linux. On Windows the occupying socket is exclusive, so the bind fails with
///     <see cref="SocketError.AccessDenied" /> (WSAEACCES). The texts come from
///     <see cref="SocketException" /> for each code, so a test does not hard-code English that
///     only one OS produces.
/// </remarks>
internal static class PortTakenReason
{
    private static readonly string[] Reasons =
    [
        new SocketException((int)SocketError.AddressAlreadyInUse).Message,
        new SocketException((int)SocketError.AccessDenied).Message,
    ];

    /// <summary>Returns true when the text contains one of the two OS reasons.</summary>
    /// <param name="text">The log line, start-failure text or health description.</param>
    public static bool IsIn(string text) =>
        text is not null
        && Reasons.Any(reason => text.Contains(reason, StringComparison.OrdinalIgnoreCase));

    /// <summary>The accepted reasons, for an assertion message.</summary>
    public static string Describe() => string.Join(" | ", Reasons);
}
