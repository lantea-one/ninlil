using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This class represents a single entry in a DNS message's Question section: what a client
///     is actually asking for. Almost every DNS message carries exactly one of these, even though
///     the wire format technically allows for more.
/// </summary>
/// <param name="Name">The name being asked about.</param>
/// <param name="Type">The record type being asked for.</param>
/// <param name="Class">The class being asked within, almost always <see cref="DnsClassEnum.Internet" />.</param>
public record DnsQuestion(DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class);
