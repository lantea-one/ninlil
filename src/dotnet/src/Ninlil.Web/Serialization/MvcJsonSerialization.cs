using Microsoft.AspNetCore.Mvc;

// Define our namespace.
namespace Ninlil.Web.Serialization;

/// <summary>
///     This class holds the one place that configures the JSON options MVC binds request bodies
///     with. <c>Program.cs</c> applies it to the controllers, and tests that replay a request
///     body without a host (the two-node cluster backfill test) build their options from it, so
///     they read a body the way the controllers do.
/// </summary>
public static class MvcJsonSerialization
{
    /// <summary>
    ///     This method adds <see cref="RequestEnumNameJsonConverterFactory" />, which makes every
    ///     request-body enum take declared names only, to the MVC JSON options.
    /// </summary>
    /// <param name="options">The MVC JSON options to configure.</param>
    public static void Configure(JsonOptions options) =>
        options.JsonSerializerOptions.Converters.Add(new RequestEnumNameJsonConverterFactory());
}
