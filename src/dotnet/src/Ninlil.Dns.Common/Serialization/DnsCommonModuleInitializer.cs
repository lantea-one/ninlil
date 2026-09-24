using System.Runtime.CompilerServices;

// Define our namespace.
namespace Ninlil.Dns.Common.Serialization;

/// <summary>
///     This class registers this project's own JSON converters with
///     <see cref="Ninlil.Common.Serializer.JsonSerializer" /> as soon as this assembly loads,
///     rather than relying on a composition root to remember to do so through dependency
///     injection. Every record data model in <c>Ninlil.Dns.Common.Wire.Records</c> is a plain
///     record deserialized directly from a stored record's own JSON wherever it is needed --
///     including from static contexts, such as a resolver's own record-to-wire-format
///     conversion, that never touch dependency injection at all -- so this converter must always
///     be present, unconditionally.
/// </summary>
internal static class DnsCommonModuleInitializer
{
    /// <summary>
    ///     This method is run automatically by the runtime the first time any type in this
    ///     assembly is used.
    /// </summary>
    /// <remarks>
    ///     This project deliberately has no dependency injection package of its own to hang a
    ///     more conventional <c>IServiceCollection</c> extension method off of -- see this
    ///     project's own description, which states it never talks to a network socket or a
    ///     database, let alone a DI container -- so a module initializer is the only way to
    ///     guarantee this converter is present unconditionally, including for a caller that
    ///     never goes through dependency injection at all.
    /// </remarks>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize() =>
        Ninlil.Common.Serializer.JsonSerializer.AddConverter<DnsNameJsonConverter>();
}
