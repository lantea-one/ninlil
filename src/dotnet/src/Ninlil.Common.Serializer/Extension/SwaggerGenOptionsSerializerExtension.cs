using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Serializer.Filter;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace
namespace Ninlil.Common.Serializer.Extension;

/// <summary>
///     This class maintains our SwaggerGenOptions extensions
/// </summary>
public static class SwaggerGenOptionsSerializerExtension
{
    /// <summary>
    ///     This method registers the Serializer FromJsonQuery and FromXmlQuery filters for Swagger/ReDoc.
    /// </summary>
    /// <param name="instance">The current instance of SwaggerGenOptions.</param>
    /// <returns>The current <paramref name="instance" /> of SwaggerGenOptions.</returns>
    public static SwaggerGenOptions AddSerializerQueryFilters(this SwaggerGenOptions instance) =>
        instance
            .AddSerializerDataUriQueryFilters()
            .AddSerializerJsonQueryFilters()
            .AddSerializerXmlQueryFilters();

    /// <summary>
    ///     This method registers the Serializer FromDataUriQuery filter for Swagger/ReDoc.
    /// </summary>
    /// <param name="instance">The current instance of SwaggerGenOptions.</param>
    /// <returns>The current <paramref name="instance" /> of SwaggerGenOptions.</returns>
    public static SwaggerGenOptions AddSerializerDataUriQueryFilters(
        this SwaggerGenOptions instance
    )
    {
        // Add the JSON operation filter.
        instance.OperationFilter<FromDataUriQueryOperationFilter>();

        // We're done, return the instance.
        return instance;
    }

    /// <summary>
    ///     This method registers the Serializer FromJsonQuery filter for Swagger/ReDoc.
    /// </summary>
    /// <param name="instance">The current instance of SwaggerGenOptions.</param>
    /// <returns>The current <paramref name="instance" /> of SwaggerGenOptions.</returns>
    public static SwaggerGenOptions AddSerializerJsonQueryFilters(this SwaggerGenOptions instance)
    {
        // Add the JSON operation filter.
        instance.OperationFilter<FromJsonQueryOperationFilter>();

        // We're done, return the instance.
        return instance;
    }

    /// <summary>
    ///     This method registers the Serializer FromXmlQuery and FromXmlQuery filters for Swagger/ReDoc.
    /// </summary>
    /// <param name="instance">The current instance of SwaggerGenOptions.</param>
    /// <returns>The current <paramref name="instance" /> of SwaggerGenOptions.</returns>
    public static SwaggerGenOptions AddSerializerXmlQueryFilters(this SwaggerGenOptions instance)
    {
        // Add the XML operation filter.
        instance.OperationFilter<FromXmlQueryOperationFilter>();

        // We're done, return the instance.
        return instance;
    }
}
