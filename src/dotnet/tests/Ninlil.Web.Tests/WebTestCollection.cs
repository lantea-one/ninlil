// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class defines the single shared xUnit test collection every test class in this
///     project belongs to, so they all share one <see cref="NinlilDnsWebApplicationFactory" />
///     instance and never run concurrently with each other. Found necessary by a real failure,
///     not by inspection: xUnit parallelizes different test classes by default, and building two
///     separate <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}" />
///     instances for the same minimal-hosting <c>Program</c> entry point concurrently corrupts
///     the shared internal state its deferred host-builder mechanism relies on, surfacing as
///     unrelated-looking failures (a unique-constraint violation seeding the bootstrap
///     administrator role, or 401s once that seeding silently failed) rather than an obvious
///     "these two are running at the same time" error.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WebTestCollection : ICollectionFixture<NinlilDnsWebApplicationFactory>
{
    /// <summary>
    ///     This constant contains the name every test class in this project references via
    ///     <c>[Collection(WebTestCollection.Name)]</c>.
    /// </summary>
    public const string Name = "Ninlil.Web";
}
