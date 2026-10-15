// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This static class builds client identifiers for tests on the shared host. The write
///     endpoints accept only hex identifiers and store them lowercase and colon-separated, so a
///     test that sends a random identifier builds it here and can then compare the response to
///     what it sent.
/// </summary>
internal static class TestClientIdentifiers
{
    /// <summary>
    ///     This method returns <paramref name="byteCount" /> random bytes as lowercase,
    ///     colon-separated hex (for example <c>3f:9a:07</c> for three), to follow a fixed prefix.
    /// </summary>
    public static string Tail(int byteCount) =>
        string.Join(
            ':',
            Guid.NewGuid().ToByteArray().Take(byteCount).Select(b => b.ToString("x2"))
        );
}
