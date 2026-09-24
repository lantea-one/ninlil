using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class proves <see cref="Dhcpv6MessageWriter" />/<see cref="Dhcpv6MessageReader" />
///     round-trip a <see cref="Dhcpv6Message" /> correctly, purely in memory -- no socket
///     involved, the DHCPv6 analogue of <see cref="Dhcpv4MessageCodecTests" />. The real
///     socket-level proof lives in <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests</c>, alongside the
///     transport that actually uses this codec.
/// </summary>
public class Dhcpv6MessageCodecTests
{
    private static Dhcpv6Message BuildSolicit()
    {
        Dhcpv6Message message = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x123456,
        };

        message.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]);
        message.SetOptionRequest([
            (ushort)Dhcpv6OptionCodeEnum.DnsRecursiveNameServer,
            (ushort)Dhcpv6OptionCodeEnum.DomainSearchList,
        ]);
        message.SetElapsedTime(TimeSpan.FromSeconds(1.5));
        message.SetRapidCommit();

        return message;
    }

    [Fact]
    public void RoundTrip_Solicit_PreservesHeaderAndSimpleOptions()
    {
        Dhcpv6Message original = BuildSolicit();

        byte[] bytes = Dhcpv6MessageWriter.Write(original);

        Assert.True(
            Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out string failureReason)
        );
        Assert.Null(failureReason);
        Assert.Equal(Dhcpv6MessageTypeEnum.Solicit, decoded.MessageType);
        Assert.Equal(original.TransactionId, decoded.TransactionId);

        Assert.True(decoded.TryGetClientIdentifier(out byte[] duid));
        Assert.Equal(
            new byte[] { 0x00, 0x01, 0x00, 0x01, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF },
            duid
        );

        Assert.True(decoded.TryGetOptionRequest(out IReadOnlyList<ushort> requestedOptionCodes));
        Assert.Equal(
            [
                (ushort)Dhcpv6OptionCodeEnum.DnsRecursiveNameServer,
                (ushort)Dhcpv6OptionCodeEnum.DomainSearchList,
            ],
            requestedOptionCodes
        );

        Assert.True(decoded.TryGetElapsedTime(out TimeSpan elapsed));
        Assert.Equal(TimeSpan.FromSeconds(1.5), elapsed);

        Assert.True(decoded.TryGetRapidCommit());
    }

    [Fact]
    public void RoundTrip_Advertise_PreservesServerIdentifierPreferenceAndDnsOptions()
    {
        Dhcpv6Message advertise = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Advertise,
            TransactionId = 7,
        };

        advertise.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);
        advertise.SetPreference(200);
        advertise.SetDnsServers([
            IPAddress.Parse("2001:db8::53"),
            IPAddress.Parse("2001:db8::153"),
        ]);
        advertise.SetDomainSearchList(["example.com", "corp.example.com"]);

        byte[] bytes = Dhcpv6MessageWriter.Write(advertise);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));

        Assert.True(decoded.TryGetServerIdentifier(out byte[] duid));
        Assert.Equal(new byte[] { 0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD }, duid);

        Assert.True(decoded.TryGetPreference(out byte preference));
        Assert.Equal(200, preference);

        Assert.True(decoded.TryGetDnsServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(
            [IPAddress.Parse("2001:db8::53"), IPAddress.Parse("2001:db8::153")],
            dnsServers
        );

        Assert.True(decoded.TryGetDomainSearchList(out IReadOnlyList<string> domains));
        Assert.Equal(["example.com", "corp.example.com"], domains);
    }

    [Fact]
    public void RoundTrip_StatusCode_PreservesCodeAndMessage()
    {
        Dhcpv6Message reply = new() { MessageType = Dhcpv6MessageTypeEnum.Reply };
        reply.SetStatusCode(
            Dhcpv6StatusCodeEnum.NoAddressesAvailable,
            "no addresses left in scope"
        );

        byte[] bytes = Dhcpv6MessageWriter.Write(reply);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetStatusCode(out Dhcpv6StatusCodeEnum statusCode, out string statusMessage)
        );
        Assert.Equal(Dhcpv6StatusCodeEnum.NoAddressesAvailable, statusCode);
        Assert.Equal("no addresses left in scope", statusMessage);
    }

    [Fact]
    public void RoundTrip_IdentityAssociationForNonTemporaryAddresses_PreservesTimersAndNestedAddress()
    {
        Dhcpv6Message reply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Reply,
            TransactionId = 99,
        };

        Dhcpv6IdentityAssociationForNonTemporaryAddresses ianaAssociation = new()
        {
            IdentityAssociationId = 0xAABBCCDD,
            T1 = TimeSpan.FromSeconds(1800),
            T2 = TimeSpan.FromSeconds(2880),
        };
        ianaAssociation.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = IPAddress.Parse("2001:db8:1::100"),
                PreferredLifetime = TimeSpan.FromSeconds(3600),
                ValidLifetime = TimeSpan.FromSeconds(7200),
            }
        );
        reply.AddIdentityAssociationForNonTemporaryAddresses(ianaAssociation);

        byte[] bytes = Dhcpv6MessageWriter.Write(reply);

        Assert.True(
            Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out string failureReason)
        );
        Assert.Null(failureReason);

        Assert.True(
            decoded.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
            )
        );
        Assert.Single(associations);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses decodedAssociation = associations[0];
        Assert.Equal(0xAABBCCDDu, decodedAssociation.IdentityAssociationId);
        Assert.Equal(TimeSpan.FromSeconds(1800), decodedAssociation.T1);
        Assert.Equal(TimeSpan.FromSeconds(2880), decodedAssociation.T2);
        Assert.Single(decodedAssociation.Addresses);
        Assert.Equal(IPAddress.Parse("2001:db8:1::100"), decodedAssociation.Addresses[0].Address);
        Assert.Equal(TimeSpan.FromSeconds(3600), decodedAssociation.Addresses[0].PreferredLifetime);
        Assert.Equal(TimeSpan.FromSeconds(7200), decodedAssociation.Addresses[0].ValidLifetime);
    }

    [Fact]
    public void RoundTrip_MultipleIdentityAssociationsForNonTemporaryAddresses_AreAllPreserved()
    {
        Dhcpv6Message request = new() { MessageType = Dhcpv6MessageTypeEnum.Request };

        request.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );
        request.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 2 }
        );

        byte[] bytes = Dhcpv6MessageWriter.Write(request);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
            )
        );
        Assert.Equal(2, associations.Count);
        Assert.Equal(1u, associations[0].IdentityAssociationId);
        Assert.Equal(2u, associations[1].IdentityAssociationId);
    }

    [Fact]
    public void RoundTrip_IdentityAssociationForPrefixDelegation_PreservesPrefixAndLength()
    {
        Dhcpv6Message reply = new() { MessageType = Dhcpv6MessageTypeEnum.Reply };

        Dhcpv6IdentityAssociationForPrefixDelegation iapdAssociation = new()
        {
            IdentityAssociationId = 42,
            T1 = TimeSpan.FromSeconds(900),
            T2 = TimeSpan.FromSeconds(1440),
        };
        iapdAssociation.Prefixes.Add(
            new Dhcpv6IdentityAssociationPrefix
            {
                Prefix = IPAddress.Parse("2001:db8:abcd::"),
                PrefixLength = 56,
                PreferredLifetime = TimeSpan.FromSeconds(3600),
                ValidLifetime = TimeSpan.FromSeconds(7200),
            }
        );
        reply.AddIdentityAssociationForPrefixDelegation(iapdAssociation);

        byte[] bytes = Dhcpv6MessageWriter.Write(reply);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetIdentityAssociationsForPrefixDelegation(
                out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
            )
        );
        Assert.Single(associations);
        Dhcpv6IdentityAssociationForPrefixDelegation decodedAssociation = associations[0];
        Assert.Equal(42u, decodedAssociation.IdentityAssociationId);
        Assert.Single(decodedAssociation.Prefixes);
        Assert.Equal(IPAddress.Parse("2001:db8:abcd::"), decodedAssociation.Prefixes[0].Prefix);
        Assert.Equal(56, decodedAssociation.Prefixes[0].PrefixLength);
    }

    [Fact]
    public void RoundTrip_IdentityAssociationForTemporaryAddresses_HasNoRenewalTimers()
    {
        Dhcpv6Message request = new() { MessageType = Dhcpv6MessageTypeEnum.Request };

        Dhcpv6IdentityAssociationForTemporaryAddresses iataAssociation = new()
        {
            IdentityAssociationId = 5,
        };
        iataAssociation.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = IPAddress.Parse("2001:db8:2::1"),
                PreferredLifetime = TimeSpan.FromSeconds(300),
                ValidLifetime = TimeSpan.FromSeconds(600),
            }
        );
        request.AddIdentityAssociationForTemporaryAddresses(iataAssociation);

        byte[] bytes = Dhcpv6MessageWriter.Write(request);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetIdentityAssociationsForTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForTemporaryAddresses> associations
            )
        );
        Assert.Single(associations);
        Assert.Equal(5u, associations[0].IdentityAssociationId);
        Assert.Single(associations[0].Addresses);
        Assert.Equal(IPAddress.Parse("2001:db8:2::1"), associations[0].Addresses[0].Address);
    }

    [Fact]
    public void Write_TransactionIdLargerThan24Bits_Throws()
    {
        Dhcpv6Message message = new() { TransactionId = 0x01000000 };

        Assert.Throws<ArgumentException>(() => Dhcpv6MessageWriter.Write(message));
    }

    [Fact]
    public void Write_RelayForwardMessageType_Throws()
    {
        Dhcpv6Message message = new() { MessageType = Dhcpv6MessageTypeEnum.RelayForward };

        Assert.Throws<ArgumentException>(() => Dhcpv6MessageWriter.Write(message));
    }

    [Fact]
    public void TryRead_RelayForwardMessageType_ReturnsFalseWithAReason()
    {
        byte[] bytes = [(byte)Dhcpv6MessageTypeEnum.RelayForward, 0, 0, 0];

        Assert.False(
            Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message message, out string failureReason)
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_BufferTooShortForHeader_ReturnsFalseWithAReason()
    {
        byte[] tooShort = new byte[2];

        Assert.False(
            Dhcpv6MessageReader.TryRead(
                tooShort,
                out Dhcpv6Message message,
                out string failureReason
            )
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_OptionHeaderRunsPastTheBuffer_ReturnsFalseWithAReason()
    {
        // A well-formed 4-byte header, immediately followed by a single dangling option-code byte
        // pair with no complete option-len -- an intentionally truncated, hostile-shaped buffer.
        byte[] bytes = [(byte)Dhcpv6MessageTypeEnum.Solicit, 0, 0, 1, 0, 1];

        Assert.False(
            Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message message, out string failureReason)
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_OptionLengthRunsPastTheBuffer_ReturnsFalseWithAReason()
    {
        // Option code 1 (Client Identifier), claiming an 8-byte value, but only 1 byte follows.
        byte[] bytes = [(byte)Dhcpv6MessageTypeEnum.Solicit, 0, 0, 1, 0, 1, 0, 8, 0xFF];

        Assert.False(
            Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message message, out string failureReason)
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryGetDomainSearchList_CompressedName_ReturnsFalse()
    {
        Dhcpv6Message message = new();
        // A label-length byte with its top two bits set is a compression pointer, which this
        // project's own decoder deliberately does not support (see
        // Dhcpv6MessageOptionExtensions.TryGetDomainSearchList's own doc comment).
        message.SetRawOption((ushort)Dhcpv6OptionCodeEnum.DomainSearchList, [0xC0, 0x00]);

        Assert.False(message.TryGetDomainSearchList(out IReadOnlyList<string> domains));
        Assert.Null(domains);
    }

    [Fact]
    public void RoundTrip_ClientFqdn_FullyQualifiedName_PreservesFlagsAndName()
    {
        Dhcpv6Message message = new() { MessageType = Dhcpv6MessageTypeEnum.Request };

        message.SetClientFqdn(
            serverShouldNotPerformAnyDnsUpdates: false,
            serverOverrodeClientPreference: false,
            serverPerformsAaaaUpdate: true,
            domainName: "workstation.example.com",
            isFullyQualified: true
        );

        byte[] bytes = Dhcpv6MessageWriter.Write(message);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetClientFqdn(
                out bool n,
                out bool o,
                out bool s,
                out string domainName,
                out bool isFullyQualified
            )
        );
        Assert.False(n);
        Assert.False(o);
        Assert.True(s);
        Assert.Equal("workstation.example.com", domainName);
        Assert.True(isFullyQualified);
    }

    [Fact]
    public void RoundTrip_ClientFqdn_PartialName_IsNotFullyQualified()
    {
        Dhcpv6Message message = new() { MessageType = Dhcpv6MessageTypeEnum.Request };

        // RFC 4704 Section 4.2: "To send a partial name, the Domain Name field is set to the
        // DNS-encoded domain name without the terminating zero-length label."
        message.SetClientFqdn(
            serverShouldNotPerformAnyDnsUpdates: false,
            serverOverrodeClientPreference: false,
            serverPerformsAaaaUpdate: false,
            domainName: "workstation",
            isFullyQualified: false
        );

        byte[] bytes = Dhcpv6MessageWriter.Write(message);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetClientFqdn(
                out _,
                out _,
                out _,
                out string domainName,
                out bool isFullyQualified
            )
        );
        Assert.Equal("workstation", domainName);
        Assert.False(isFullyQualified);
    }

    [Fact]
    public void RoundTrip_ClientFqdn_EmptyDomainName_DecodesAsNull()
    {
        Dhcpv6Message message = new() { MessageType = Dhcpv6MessageTypeEnum.Request };

        // RFC 4704 Section 4.2: "A client MAY also leave the Domain Name field empty if it
        // desires the server to provide a name."
        message.SetClientFqdn(
            serverShouldNotPerformAnyDnsUpdates: false,
            serverOverrodeClientPreference: false,
            serverPerformsAaaaUpdate: true,
            domainName: null,
            isFullyQualified: false
        );

        byte[] bytes = Dhcpv6MessageWriter.Write(message);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetClientFqdn(
                out _,
                out _,
                out _,
                out string domainName,
                out bool isFullyQualified
            )
        );
        Assert.Null(domainName);
        Assert.False(isFullyQualified);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void RoundTrip_ClientFqdn_EveryFlagCombination_IsPreservedExactly(bool n, bool o, bool s)
    {
        Dhcpv6Message message = new() { MessageType = Dhcpv6MessageTypeEnum.Reply };

        message.SetClientFqdn(
            serverShouldNotPerformAnyDnsUpdates: n,
            serverOverrodeClientPreference: o,
            serverPerformsAaaaUpdate: s,
            domainName: "host",
            isFullyQualified: true
        );

        byte[] bytes = Dhcpv6MessageWriter.Write(message);

        Assert.True(Dhcpv6MessageReader.TryRead(bytes, out Dhcpv6Message decoded, out _));
        Assert.True(
            decoded.TryGetClientFqdn(
                out bool decodedN,
                out bool decodedO,
                out bool decodedS,
                out _,
                out _
            )
        );
        Assert.Equal(n, decodedN);
        Assert.Equal(o, decodedO);
        Assert.Equal(s, decodedS);
    }

    [Fact]
    public void TryGetClientFqdn_OptionAbsent_ReturnsFalse()
    {
        Dhcpv6Message message = new();

        Assert.False(
            message.TryGetClientFqdn(
                out _,
                out _,
                out _,
                out string domainName,
                out bool isFullyQualified
            )
        );
        Assert.Null(domainName);
        Assert.False(isFullyQualified);
    }

    [Fact]
    public void TryGetClientFqdn_CompressedName_ReturnsFalse()
    {
        Dhcpv6Message message = new();
        // Flags octet (0x00) followed by a compression-pointer-shaped label length -- this
        // project's own decoder never resolves an RFC 1035 Section 4.1.4 compression pointer for
        // any DHCPv6 option, the same restriction TryGetDomainSearchList already documents.
        message.SetRawOption((ushort)Dhcpv6OptionCodeEnum.ClientFqdn, [0x00, 0xC0, 0x00]);

        Assert.False(message.TryGetClientFqdn(out _, out _, out _, out string domainName, out _));
        Assert.Null(domainName);
    }

    [Fact]
    public void TryGetClientFqdn_TruncatedLabel_ReturnsFalse()
    {
        Dhcpv6Message message = new();
        // Flags octet (0x00), then a label claiming 10 bytes with only 4 actually present.
        message.SetRawOption(
            (ushort)Dhcpv6OptionCodeEnum.ClientFqdn,
            [0x00, 10, (byte)'h', (byte)'o', (byte)'s', (byte)'t']
        );

        Assert.False(message.TryGetClientFqdn(out _, out _, out _, out string domainName, out _));
        Assert.Null(domainName);
    }

    [Fact]
    public void TryGetClientFqdn_TrailingBytesAfterRootLabel_ReturnsFalse()
    {
        Dhcpv6Message message = new();
        // Flags octet (0x00), a fully-qualified "a" (one label, then the root label), then one
        // dangling extra byte this option's own single-name shape never permits.
        message.SetRawOption(
            (ushort)Dhcpv6OptionCodeEnum.ClientFqdn,
            [0x00, 1, (byte)'a', 0x00, 0xFF]
        );

        Assert.False(message.TryGetClientFqdn(out _, out _, out _, out string domainName, out _));
        Assert.Null(domainName);
    }

    [Fact]
    public void UserClass_RoundTrip_PreservesEveryInstanceInOrder()
    {
        Dhcpv6Message message = new();
        message.SetUserClasses(["guest", "iot"]);

        Assert.True(message.TryGetUserClasses(out IReadOnlyList<string> userClasses));
        Assert.Equal(["guest", "iot"], userClasses);
    }

    [Fact]
    public void TryGetUserClasses_OptionAbsent_ReturnsFalse()
    {
        Dhcpv6Message message = new();

        Assert.False(message.TryGetUserClasses(out IReadOnlyList<string> userClasses));
        Assert.Null(userClasses);
    }

    [Fact]
    public void TryGetUserClasses_TruncatedInstance_StopsAtLastWholeInstance()
    {
        Dhcpv6Message message = new();
        // One whole "ok" instance (2-octet length 2, then "ok"), then a length field (5) claiming
        // more data than actually follows.
        message.SetRawOption(
            (ushort)Dhcpv6OptionCodeEnum.UserClass,
            [0x00, 0x02, (byte)'o', (byte)'k', 0x00, 0x05, (byte)'x']
        );

        Assert.True(message.TryGetUserClasses(out IReadOnlyList<string> userClasses));
        Assert.Equal(["ok"], userClasses);
    }

    [Fact]
    public void VendorClass_RoundTrip_PreservesEnterpriseNumberAndEveryInstanceInOrder()
    {
        Dhcpv6Message message = new();
        message.SetVendorClass(32473, ["PXEClient:Arch:00000", "PXEClient:Arch:00007"]);

        Assert.True(
            message.TryGetVendorClass(
                out uint enterpriseNumber,
                out IReadOnlyList<string> vendorClassData
            )
        );
        Assert.Equal(32473u, enterpriseNumber);
        Assert.Equal(["PXEClient:Arch:00000", "PXEClient:Arch:00007"], vendorClassData);
    }

    [Fact]
    public void VendorClass_WithNoVendorClassDataInstances_StillReportsTheEnterpriseNumber()
    {
        Dhcpv6Message message = new();
        message.SetVendorClass(32473, []);

        Assert.True(
            message.TryGetVendorClass(
                out uint enterpriseNumber,
                out IReadOnlyList<string> vendorClassData
            )
        );
        Assert.Equal(32473u, enterpriseNumber);
        Assert.Empty(vendorClassData);
    }

    [Fact]
    public void TryGetVendorClass_OptionAbsent_ReturnsFalse()
    {
        Dhcpv6Message message = new();

        Assert.False(message.TryGetVendorClass(out _, out IReadOnlyList<string> vendorClassData));
        Assert.Null(vendorClassData);
    }

    [Fact]
    public void TryGetVendorClass_TooShortForEnterpriseNumber_ReturnsFalse()
    {
        Dhcpv6Message message = new();
        message.SetRawOption((ushort)Dhcpv6OptionCodeEnum.VendorClass, [0x00, 0x00, 0x7E]);

        Assert.False(message.TryGetVendorClass(out _, out IReadOnlyList<string> vendorClassData));
        Assert.Null(vendorClassData);
    }
}
