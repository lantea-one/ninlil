using Ninlil.Web.OpenApi;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="XmlCrefText.Render" /> on its own, with no host: plain strings
///     in, plain strings out. The action map and the type test are small fakes, so each rule (an
///     action becomes its route, a method falls back to <c>Type.Member</c>, a type or member loses
///     its namespace) is checked against a fixed input.
/// </summary>
public class XmlCrefTextTests
{
    private static readonly Dictionary<string, string> Actions = new()
    {
        ["Ninlil.Web.Controllers.AuthController.Login"] = "POST /api/auth/login",
        ["Ninlil.Web.Controllers.AuthController.Logout"] = "POST /api/auth/logout",
    };

    /// <summary>
    ///     This fake knows the types the tests mention; a namespace is not a type.
    /// </summary>
    private static readonly HashSet<string> Types =
    [
        "Ninlil.Web.Models.ClusterNodeWriteRequest",
        "Ninlil.Web.Models.ClusterNodeDetail",
        "Ninlil.Web.Models.Outer",
        "Ninlil.Web.Models.Outer.Inner",
        "Ninlil.Dns.Server.Dnssec.IDnssecKeyManagementService",
        "Ninlil.Dns.Server.Presets.PresetConfigurationFileApplier",
        "Ninlil.Web.Controllers.AuthController",
        "Ninlil.Dns.Common.Wire.Box",
    ];

    private static string Render(string text) => XmlCrefText.Render(text, Actions, Types.Contains);

    [Fact]
    public void Render_ForAnActionIdWithParameters_ReturnsTheOperation()
    {
        string rendered = Render(
            "see M:Ninlil.Web.Controllers.AuthController.Login(Ninlil.Web.Models.LoginRequest). Done"
        );

        Assert.Equal("see `POST /api/auth/login`. Done", rendered);
    }

    [Fact]
    public void Render_ForTwoIdsInOneSentence_ReplacesBoth()
    {
        string rendered = Render(
            "Use M:Ninlil.Web.Controllers.AuthController.Login(System.String) or "
                + "M:Ninlil.Web.Controllers.AuthController.Logout, not both."
        );

        Assert.Equal("Use `POST /api/auth/login` or `POST /api/auth/logout`, not both.", rendered);
    }

    [Fact]
    public void Render_ForAServiceMethodId_ReturnsTypeDotMember()
    {
        string rendered = Render(
            "(see M:Ninlil.Dns.Server.Dnssec.IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync(System.Guid,System.Threading.CancellationToken))."
        );

        Assert.Equal("(see `IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync`).", rendered);
    }

    [Fact]
    public void Render_ForATypeName_ReturnsTheTypeName()
    {
        string rendered = Render(
            "through Ninlil.Dns.Server.Presets.PresetConfigurationFileApplier. A field"
        );

        Assert.Equal("through `PresetConfigurationFileApplier`. A field", rendered);
    }

    [Fact]
    public void Render_ForANestedType_KeepsOuterAndInner()
    {
        string rendered = Render("see Ninlil.Web.Models.Outer.Inner.");

        Assert.Equal("see `Outer.Inner`.", rendered);
    }

    [Fact]
    public void Render_ForAGenericType_DropsTheArity()
    {
        string rendered = Render("see T:Ninlil.Dns.Common.Wire.Box`1 here");

        Assert.Equal("see `Box` here", rendered);
    }

    [Fact]
    public void Render_ForAProperty_ReturnsTypeDotProperty()
    {
        string rendered = Render("in place of Ninlil.Web.Models.ClusterNodeDetail.PasswordSet.");

        Assert.Equal("in place of `ClusterNodeDetail.PasswordSet`.", rendered);
    }

    [Fact]
    public void Render_ForAnEnumField_ReturnsTypeDotField()
    {
        string rendered = Render("F:Ninlil.Web.Models.Outer.Inner.Alpha is the default");

        Assert.Equal("`Outer.Inner.Alpha` is the default", rendered);
    }

    [Fact]
    public void Render_ForAnActionWithAnAmbiguousName_FallsBackToTypeDotMember()
    {
        // Not in the map: the filter leaves out a name that serves several routes.
        string rendered = Render(
            "see M:Ninlil.Web.Controllers.AuthController.Refresh(System.String)"
        );

        Assert.Equal("see `AuthController.Refresh`", rendered);
    }

    [Fact]
    public void Render_ForAConstructorId_ReturnsTheType()
    {
        string rendered = Render("M:Ninlil.Web.Models.ClusterNodeWriteRequest.#ctor");

        Assert.Equal("`ClusterNodeWriteRequest`", rendered);
    }

    [Fact]
    public void Render_ForANameAlreadyInBackticks_KeepsASinglePair()
    {
        string rendered = Render("see `Ninlil.Web.Models.ClusterNodeWriteRequest`.");

        Assert.Equal("see `ClusterNodeWriteRequest`.", rendered);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Returns 404 for an unknown id. Requires `zones:write`.")]
    [InlineData("A GET /api/zones call; see RFC 7396 and Ninlil as a product name.")]
    public void Render_ForTextWithoutCrefs_ReturnsItUnchanged(string text)
    {
        Assert.Equal(text, Render(text));
    }

    [Fact]
    public void Render_ForNull_ReturnsNull()
    {
        Assert.Null(Render(null));
    }
}
