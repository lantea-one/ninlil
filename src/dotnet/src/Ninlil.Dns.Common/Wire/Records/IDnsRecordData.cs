// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This interface is implemented by every typed record data model in this namespace
///     (<see cref="ARecordData" />, <see cref="SoaRecordData" />, and so on). It has no members. It
///     gives code that handles decoded record data of any type one common type to hold, as
///     <see cref="Wire.DnsResourceRecord" /> does for its data.
/// </summary>
public interface IDnsRecordData;
