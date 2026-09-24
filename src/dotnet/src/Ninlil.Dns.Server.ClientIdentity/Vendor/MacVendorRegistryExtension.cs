// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This class provides the fixed facts about each <see cref="MacVendorRegistryEnum" /> member:
///     its prefix length and the registry name IEEE's own CSV files use in their first column.
/// </summary>
public static class MacVendorRegistryExtension
{
    /// <summary>
    ///     This property contains every registry, in lookup order from shortest to longest prefix.
    /// </summary>
    public static IReadOnlyList<MacVendorRegistryEnum> All { get; } =
    [MacVendorRegistryEnum.MaL, MacVendorRegistryEnum.MaM, MacVendorRegistryEnum.MaS];

    extension(MacVendorRegistryEnum registry)
    {
        /// <summary>
        ///     This property contains the registry's prefix length, in bits.
        /// </summary>
        public int PrefixBits =>
            registry switch
            {
                MacVendorRegistryEnum.MaL => 24,
                MacVendorRegistryEnum.MaM => 28,
                MacVendorRegistryEnum.MaS => 36,
                _ => throw new ArgumentOutOfRangeException(nameof(registry)),
            };

        /// <summary>
        ///     This property contains the registry's prefix length in hexadecimal digits -- the
        ///     exact length of every value in its CSV file's <c>Assignment</c> column.
        /// </summary>
        public int PrefixHexDigits => registry.PrefixBits / 4;

        /// <summary>
        ///     This property contains the registry's own name as IEEE's CSV files spell it in their
        ///     <c>Registry</c> column (for example <c>MA-L</c>).
        /// </summary>
        public string IeeeName =>
            registry switch
            {
                MacVendorRegistryEnum.MaL => "MA-L",
                MacVendorRegistryEnum.MaM => "MA-M",
                MacVendorRegistryEnum.MaS => "MA-S",
                _ => throw new ArgumentOutOfRangeException(nameof(registry)),
            };
    }

    /// <summary>
    ///     This method finds the registry whose prefixes are exactly <paramref name="hexDigits" />
    ///     hexadecimal digits long.
    /// </summary>
    /// <param name="hexDigits">A prefix's length in hexadecimal digits.</param>
    /// <param name="registry">The matching registry, when one exists.</param>
    /// <returns><see langword="true" /> when a registry uses that prefix length.</returns>
    public static bool TryFromPrefixHexDigits(int hexDigits, out MacVendorRegistryEnum registry)
    {
        foreach (MacVendorRegistryEnum candidate in All)
        {
            if (candidate.PrefixHexDigits != hexDigits)
                continue;

            registry = candidate;
            return true;
        }

        registry = default;
        return false;
    }
}
