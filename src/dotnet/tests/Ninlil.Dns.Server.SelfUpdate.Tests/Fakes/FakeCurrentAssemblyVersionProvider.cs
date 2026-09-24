namespace Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

/// <summary>
///     This class stands in for <see cref="DefaultCurrentAssemblyVersionProvider" /> in a test,
///     reporting a fixed, deterministic version instead of whatever the real test host assembly
///     happens to carry -- see <see cref="ICurrentAssemblyVersionProvider" />'s own doc comment
///     for why this project extracted this behind an interface at all.
/// </summary>
/// <param name="version">The fixed version this provider always reports.</param>
public sealed class FakeCurrentAssemblyVersionProvider(Version version)
    : ICurrentAssemblyVersionProvider
{
    /// <inheritdoc />
    public Version GetCurrentVersion() => version;
}
