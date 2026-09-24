using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

// Define our namespace for the Network ACL (NAC) string model.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class represents a model for Network ACL (NAC) string values, extending the StructuredStringModel.
///     A NAC can be an IP address or network address to allow, prefixed with '!' to deny access,
///     or set to 'false' to remove existing values.
/// </summary>
[XmlRoot("networkAcl")]
public class NetworkAclModel : StructuredStringModel
{
    /// <summary>
    ///     This static field contains the model's regular expression pattern for matching an ACL rule.
    ///     It captures the exact string "false" for removal, or an optional "!" deny prefix followed
    ///     by an IPv4/IPv6 address and an optional CIDR subnet mask.
    /// </summary>
    public static readonly Regex Pattern = new(
        $"^(?<remove>false)$|^(?<deny>!)?{NetworkAddressModel.Pattern.ToString()[1..]}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    ///     This property represents the IP address associated with the ACL rule.
    /// </summary>
    public NetworkAddressModel Address { get; private set; }

    /// <summary>
    ///     Indicates whether this rule is configured to allow access (not prefixed with '!' and not set to 'false').
    /// </summary>
    public bool Allow => !Deny && !Remove;

    /// <summary>
    ///     Indicates whether this rule is configured to deny access (prefixed with '!').
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public bool Deny { get; private set; }

    /// <summary>
    ///     Indicates whether this rule is configured to remove existing values (set to 'false').
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public bool Remove { get; private set; }

    /// <summary>
    ///     This constructor initializes a new empty instance of the <see cref="NetworkAclModel" /> class.
    /// </summary>
    public NetworkAclModel()
        : base(true, Pattern) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="NetworkAclModel" /> class
    ///     with a specified string <paramref name="value" />.
    /// </summary>
    /// <param name="value">The string value to initialize the model with.</param>
    public NetworkAclModel(string value)
        : base(true, value, Pattern) { }

    /// <summary>
    ///     This method determines whether the current instance of the <see cref="NetworkAclModel" />
    ///     is valid based on its properties.
    /// </summary>
    /// <returns><see langword="true"/> if the model is valid; otherwise, <see langword="false"/>.</returns>
    public override bool IsValid() => Remove || Address?.IsValid() is true;

    /// <summary>
    ///     This method parses the provided <paramref name="original" /> string and the corresponding
    ///     <paramref name="match" /> from the regex patterns to populate the properties of the
    ///     <see cref="NetworkAclModel" /> instance.
    /// </summary>
    /// <param name="original">The original string that was matched.</param>
    /// <param name="match">The regex match containing the captured groups.</param>
    /// <returns>A populated model instance, or <see langword="null" /> if parsing fails.</returns>
    protected override StructuredStringModel ParseMatch(string original, Match match)
    {
        // Check if the rule is an explicit command to remove existing values.
        if (
            match.Groups.TryGetValue("remove", out var remove)
            && remove is { Success: true, Value: "false" }
        )
        {
            // Reset the remove flag into the instance.
            Remove = true;

            // We're one, return the instance as valid since no further parsing is needed for a remove command.
            return this;
        }

        // Otherwise, force the remove flag to false since we are not in a remove command context.
        Remove = false;

        // Check if the rule is prefixed with the deny operator.
        Deny =
            match.Groups.TryGetValue("deny", out var deny) && deny is { Success: true, Value: "!" };

        // Localize the IP address and CIDR mask from the regex match groups.
        var ipAddress =
            match.Groups.TryGetValue("ip", out var ip)
            && ip.ToString().Trim() is not (null or { Length: 0 })
                ? ip.ToString().Trim()
                : null;

        // If the IP address is null, then we cannot proceed with parsing, so return null to indicate failure.
        if (ipAddress?.Trim() is null or { Length: 0 })
            return null;

        // Localize the CIDR mask from the regex match groups, if present.
        int? cidrMask =
            match.Groups.TryGetValue("cidr", out var cidr)
            && cidr.ToString().Trim() is not (null or { Length: 0 })
                ? int.Parse(cidr.ToString().Trim())
                : null;

        // If we can't parse the IP address and CIDR mask into a valid
        // NetworkAddressModel, return null to indicate failure.
        if (
            !TryParse(
                cidrMask.HasValue ? $"{ipAddress.Trim()}/{cidr.Value}" : ipAddress.Trim(),
                out NetworkAddressModel networkAddress
            )
        )
            return null;

        // Otherwise, set the NetworkAddress property to the successfully parsed instance.
        Address = networkAddress;

        // We're done, return the parsed and populated instance of the model.
        return this;
    }

    /// <summary>
    ///     This method returns a string representation of the <see cref="NetworkAclModel" />
    ///     based on the provided <paramref name="options" /> dictionary.
    /// </summary>
    protected override string ToString(IDictionary<string, object> options)
    {
        // If the configuration explicitly dictates removal, return "false".
        if (Remove)
            return "false";

        // If the IP address is not set, we cannot construct a valid string representation, so return null.
        if (Address is null)
            return null;

        // Localize the includeCidr option from the provided options dictionary, defaulting to true if not specified.
        var includeCidr =
            !options.TryGetValue("includeCidr", out var includeCidrFlag)
            || includeCidrFlag is not bool includeCidrBool
            || includeCidrBool;

        // Localize the includeDeny option from the provided options dictionary, defaulting to true if not specified.
        var includeDeny =
            !options.TryGetValue("includeDeny", out var includeDenyFlag)
            || includeDenyFlag is not bool includeDenyBool
            || includeDenyBool;

        // Localize the includeIp option from the provided options dictionary, defaulting to true if not specified.
        var includeIp =
            !options.TryGetValue("includeIp", out var includeIpFlag)
            || includeIpFlag is not bool includeIpBool
            || includeIpBool;

        // Initialize an empty string to build the final representation.
        var result = string.Empty;

        // Prepend the deny operator if required.
        if (includeDeny && Deny)
            result += "!";

        // Append the string representation of the IP address.
        if (includeIp || includeCidr)
            result += Address.ToString(includeIp, includeCidr);

        // We're done, return the constructed string representation of the ACL rule.
        return result;
    }

    /// <summary>
    ///     This method returns a string representation of the <see cref="NetworkAclModel" />
    ///     based on the specified <paramref name="includeDeny" />, <paramref name="includeIp" />,
    ///    and <paramref name="includeCidr" /> values.
    /// </summary>
    /// <param name="includeDeny">
    ///     A <see cref="bool" /> indicating whether to include the deny operator in the string representation.
    /// </param>
    /// <param name="includeIp">
    ///     A <see cref="bool" /> indicating whether to include the IP address in the string representation.
    /// </param>
    /// <param name="includeCidr">
    ///     A <see cref="bool" /> indicating whether to include the CIDR subnet mask in the string representation.
    /// </param>
    /// <returns>
    ///     The string representation of the <see cref="NetworkAclModel" /> based on the specified options.
    /// </returns>
    public string ToString(bool includeDeny, bool includeIp, bool includeCidr) =>
        ToString(
            new Dictionary<string, object>
            {
                { "includeCidr", includeCidr },
                { "includeDeny", includeDeny },
                { "includeIp", includeIp },
            }
        );

    /// <summary>
    ///     This method returns a default string representation of the <see cref="NetworkAclModel" />.
    /// </summary>
    /// <returns>
    ///     The default string representation of the <see cref="NetworkAclModel" /> with all components included.
    /// </returns>
    public override string ToString() => ToString(true, true, true);
}
