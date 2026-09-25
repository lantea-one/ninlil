using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents one entry in the Question section of a DNS message: what the client
///     is asking for. Almost every message carries one, although the wire format allows more.
/// </summary>
/// <param name="Name">The name being asked about.</param>
/// <param name="Type">The record type being asked for.</param>
/// <param name="Class">The class being asked within, almost always <see cref="DnsClassEnum.Internet" />.</param>
public record DnsQuestion(DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class);
