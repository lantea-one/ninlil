// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class defines the xUnit test collection that every test class in this project joins,
///     so they all share one <see cref="NinlilDnsWebApplicationFactory" /> and never run
///     concurrently. xUnit runs test classes in parallel by default, and building two
///     <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" />
///     instances for the minimal-hosting <c>Program</c> at the same time corrupts the shared
///     state of its deferred host builder. The symptoms look unrelated: a unique-constraint
///     violation while seeding the bootstrap administrator role, or 401s after that seeding
///     failed. The factory's environment variables are process-wide as well.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WebTestCollection : ICollectionFixture<NinlilDnsWebApplicationFactory>
{
    /// <summary>
    ///     This constant contains the name every test class in this project references through
    ///     <c>[Collection(WebTestCollection.Name)]</c>.
    /// </summary>
    public const string Name = "Ninlil.Web";
}
