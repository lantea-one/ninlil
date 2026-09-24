// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This interface is implemented by every strongly-typed record data model in this namespace
///     -- <see cref="ARecordData" />, <see cref="SoaRecordData" />, and so on. It carries no
///     members of its own; it exists purely so that code working with "some record's decoded
///     data, whatever type it turns out to be" has a single, common type to hold onto, the same
///     way <see cref="Wire.DnsResourceRecord" /> holds an owner name and type-specific data
///     without needing to know, at compile time, which of the many possible shapes that data
///     takes.
/// </summary>
public interface IDnsRecordData;
