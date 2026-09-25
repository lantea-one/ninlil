// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

/// <summary>
///     This class replaces <see cref="DefaultCurrentAssemblyVersionProvider" /> in tests and
///     reports a fixed version instead of the test host's.
/// </summary>
/// <param name="version">The fixed version this provider always reports.</param>
public sealed class FakeCurrentAssemblyVersionProvider(Version version)
    : ICurrentAssemblyVersionProvider
{
    /// <inheritdoc />
    public Version GetCurrentVersion() => version;
}
