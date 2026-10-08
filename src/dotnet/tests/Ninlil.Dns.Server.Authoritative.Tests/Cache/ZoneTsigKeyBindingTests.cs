using Ninlil.Dns.Server.Authoritative.Cache;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Cache;

/// <summary>
///     This class tests <see cref="ZoneTsigKeyBinding" />, the name comparison behind the
///     per-zone update and transfer key lists. It needs no host.
/// </summary>
public class ZoneTsigKeyBindingTests
{
    [Fact]
    public void IsAllowed_WithAnEmptyList_AcceptsNoKey()
    {
        Assert.False(ZoneTsigKeyBinding.IsAllowed([], "any-key"));
    }

    [Fact]
    public void IsAllowed_WithANullList_AcceptsNoKey()
    {
        Assert.False(ZoneTsigKeyBinding.IsAllowed(null, "any-key"));
    }

    [Theory]
    [InlineData("Secondary-Key", "secondary-key")]
    [InlineData("secondary-key", "SECONDARY-KEY")]
    [InlineData("secondary-key.", "secondary-key")]
    [InlineData("secondary-key", "secondary-key.")]
    public void IsAllowed_ForTheSameNameInAnotherCaseOrWithATrailingDot_Accepts(
        string listed,
        string signer
    )
    {
        Assert.True(ZoneTsigKeyBinding.IsAllowed([listed], signer));
    }

    [Fact]
    public void IsAllowed_ForAnUnlistedKeyOrAMissingName_Refuses()
    {
        Assert.False(ZoneTsigKeyBinding.IsAllowed(["a-key"], "b-key"));
        Assert.False(ZoneTsigKeyBinding.IsAllowed(["a-key"], null));
        Assert.False(ZoneTsigKeyBinding.IsAllowed(["a-key"], ""));
    }
}
