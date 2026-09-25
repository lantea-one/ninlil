using System.Runtime.CompilerServices;

// Define our namespace.
namespace Ninlil.Dns.Common.Serialization;

/// <summary>
///     This class registers the JSON converters of this assembly with
///     <see cref="Ninlil.Common.Serializer.JsonSerializer" /> when the assembly is first used, instead of
///     relying on a composition root to do it. The record data models in
///     <c>Ninlil.Dns.Common.Wire.Records</c> are deserialized from stored JSON in many places,
///     including static code with no dependency injection, so the converter must always be
///     present.
/// </summary>
internal static class DnsCommonModuleInitializer
{
    /// <summary>
    ///     This method is run by the runtime the first time any type in this assembly is used.
    /// </summary>
    /// <remarks>
    ///     This project has no dependency injection package to hang an <c>IServiceCollection</c>
    ///     extension on, so a module initializer is the way to guarantee the converter is present,
    ///     including for callers that never use dependency injection.
    /// </remarks>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize() =>
        Ninlil.Common.Serializer.JsonSerializer.AddConverter<DnsNameJsonConverter>();
}
