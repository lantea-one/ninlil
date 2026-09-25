using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class represents a model for a bare network address string value, extending
///     <see cref="StructuredStringModel" />: an IP address, optionally with a CIDR routing prefix
///     (subnet mask) suffix (e.g. <c>10.0.0.0/24</c>). Unlike <see cref="NetworkAclModel" />, it has
///     no allow/deny/remove prefix; it is only an address.
/// </summary>
[XmlRoot("networkAddress")]
public partial class NetworkAddressModel : StructuredStringModel
{
    /// <summary>
    ///     This method returns the source-generated pattern behind <see cref="Pattern" />.
    /// </summary>
    /// <returns>The compiled network address <see cref="Regex" />.</returns>
    [GeneratedRegex(
        @"^(?<ip>[a-fA-F0-9\.:]+)(?:/(?<cidr>\d{1,3}))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        "en-US"
    )]
    private static partial Regex RegexPattern();

    /// <summary>
    ///     This static field contains the model's regular expression pattern for matching
    ///     a network address. It captures the IP address with an optional CIDR subnet mask.
    /// </summary>
    public static readonly Regex Pattern = RegexPattern();

    /// <summary>
    ///     Contains the parsed IP address or network boundary.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public IPAddress IpAddress { get; private set; }

    /// <summary>
    ///     Contains the CIDR routing prefix (subnet mask) if a network address was provided.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public int? CidrMask { get; private set; }

    /// <summary>
    ///     This constructor initializes a new empty instance of the <see cref="NetworkAddressModel" /> class.
    /// </summary>
    public NetworkAddressModel()
        : base(true, Pattern) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="NetworkAddressModel" /> class
    ///     with a specified string <paramref name="value" />.
    /// </summary>
    /// <param name="value">The string value to initialize the model with.</param>
    public NetworkAddressModel(string value)
        : base(true, value, Pattern) { }

    /// <summary>
    ///     This method determines whether the current instance of the <see cref="NetworkAddressModel" />
    ///     is valid based on its properties.
    /// </summary>
    /// <returns><see langword="true"/> if the model is valid; otherwise, <see langword="false"/>.</returns>
    public override bool IsValid()
    {
        // We're done, determine the validity of the model based on the IP address family and the CIDR mask.
        return IpAddress switch
        {
            // If the IP address is null, the model is invalid.
            null => false,

            // If the IP address is IPv4, the CIDR mask must be between 0 and 32.
            { AddressFamily: AddressFamily.InterNetwork } when CidrMask is < 0 or > 32 => false,

            // If the IP address is IPv6, the CIDR mask must be between 0 and 128.
            { AddressFamily: AddressFamily.InterNetworkV6 } when CidrMask is < 0 or > 128 => false,

            // Otherwise, the model is valid.
            _ => true,
        };
    }

    /// <summary>
    ///     This method parses the provided <paramref name="original" /> string and the corresponding
    ///     <paramref name="match" /> from the regex patterns to populate the properties of the
    ///     <see cref="NetworkAddressModel" /> instance.
    /// </summary>
    /// <param name="original">The original string that was matched.</param>
    /// <param name="match">The regex match containing the captured groups.</param>
    /// <returns>A populated model instance, or <see langword="null" /> if parsing fails.</returns>
    protected override StructuredStringModel ParseMatch(string original, Match match)
    {
        // Without a parseable IP address there is nothing to populate, so return null.
        if (
            !match.Groups.TryGetValue("ip", out var ip)
            || ip is not { Success: true }
            || !IPAddress.TryParse(ip.Value.Trim(), out var ipAddress)
        )
            return null;

        // Otherwise, set the parsed IP address into the instance.
        IpAddress = ipAddress;

        // Attempt to parse the optional CIDR block if a network address was provided instead of a single IP.
        CidrMask =
            match.Groups.TryGetValue("cidr", out var cidr)
            && cidr is { Success: true }
            && int.TryParse(cidr.Value.Trim(), out var cidrValue)
                ? cidrValue
                : null;

        // We're done, return the parsed and populated instance of the model.
        return this;
    }

    /// <summary>
    ///     This method returns a string representation of the <see cref="NetworkAddressModel" />
    ///     based on the provided <paramref name="options" /> dictionary.
    /// </summary>
    protected override string ToString(IDictionary<string, object> options)
    {
        // If the IP address is not set, we cannot construct a valid string representation, so return null.
        if (IpAddress is null)
            return null;

        // Localize the includeCidr option from the provided options dictionary, defaulting to true if not specified.
        var includeCidr =
            !options.TryGetValue("includeCidr", out var includeCidrFlag)
            || includeCidrFlag is not bool includeCidrBool
            || includeCidrBool;

        // Localize the includeIp option from the provided options dictionary, defaulting to true if not specified.
        var includeIp =
            !options.TryGetValue("includeIp", out var includeIpFlag)
            || includeIpFlag is not bool includeIpBool
            || includeIpBool;

        // Initialize an empty string to build the final representation.
        var result = string.Empty;

        // Append the string representation of the IP address.
        if (includeIp)
            result += IpAddress.ToString();

        // Append the CIDR mask to formulate the complete network address.
        if (includeCidr && CidrMask.HasValue)
            result += $"/{CidrMask.Value}";

        // We're done, return the constructed string representation of the network address.
        return result;
    }

    /// <summary>
    ///     This method returns a string representation of the <see cref="NetworkAddressModel" />
    ///     based on the specified <paramref name="includeCidr" /> and <paramref name="includeIp" /> values.
    /// </summary>
    /// <param name="includeIp">
    ///     A <see cref="bool" /> indicating whether to include the IP address in the string representation.
    /// </param>
    /// <param name="includeCidr">
    ///     A <see cref="bool" /> indicating whether to include the CIDR subnet mask in the string representation.
    /// </param>
    /// <returns>
    ///     The string representation of the <see cref="NetworkAddressModel" /> based on the specified options.
    /// </returns>
    public string ToString(bool includeIp, bool includeCidr) =>
        ToString(
            new Dictionary<string, object>
            {
                { "includeCidr", includeCidr },
                { "includeIp", includeIp },
            }
        );

    /// <summary>
    ///     This method returns a default string representation of the <see cref="NetworkAddressModel" />.
    /// </summary>
    /// <returns>
    ///     The default string representation of the
    ///     <see cref="NetworkAddressModel" /> with all components included.
    /// </returns>
    public override string ToString() => ToString(true, true);
}
