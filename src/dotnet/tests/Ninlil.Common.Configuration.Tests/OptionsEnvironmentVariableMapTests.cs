using Ninlil.Common.Configuration;

// Define our namespace.
namespace Ninlil.Common.Configuration.Tests;

/// <summary>
///     These fake "Options" classes stand in for a real bound configuration class, exercising
///     exactly the shapes <see cref="OptionsEnvironmentVariableMap" /> must handle correctly: a
///     scalar leaf property, a nested complex-object property reused by two different parents (the
///     same real shape <c>TlsCertificateOptions</c> has in the real application), and a
///     list-valued property that must be skipped rather than mapped.
/// </summary>
file sealed class FakeNestedOptions
{
    public string PfxPath { get; set; } = "";
    public int ReloadSeconds { get; set; }
}

file sealed class FakeParentOptionsA
{
    public const string SectionName = "Fake:ParentA";

    public bool Enable { get; set; }
    public FakeNestedOptions Certificate { get; set; } = new();
    public List<string> KnownProxies { get; set; } = [];
}

file sealed class FakeParentOptionsB
{
    public const string SectionName = "Fake:ParentB";

    public FakeNestedOptions Certificate { get; set; } = new();
}

file sealed class FakeOptionsWithNoSectionNameConstant
{
    public string DisplayName { get; set; } = "";
}

file sealed class FakeCollidingOptionsOne
{
    public const string SectionName = "Fake:Colliding:HttpPort";

    public int Value { get; set; }
}

file sealed class FakeCollidingOptionsTwo
{
    public const string SectionName = "Fake:Colliding";

    public int HttpPortValue { get; set; }
}

public sealed class OptionsEnvironmentVariableMapTests
{
    [Fact]
    public void BuildMappings_MapsAScalarLeafProperty()
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings([
            new OptionsEnvironmentVariableRoot(typeof(FakeParentOptionsA)),
        ]);

        Assert.Equal("Fake:ParentA:Enable", mappings["NINLIL_FAKE_PARENT_A_ENABLE"]);
    }

    [Fact]
    public void BuildMappings_RecursesIntoANestedComplexProperty()
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings([
            new OptionsEnvironmentVariableRoot(typeof(FakeParentOptionsA)),
        ]);

        Assert.Equal(
            "Fake:ParentA:Certificate:PfxPath",
            mappings["NINLIL_FAKE_PARENT_A_CERTIFICATE_PFX_PATH"]
        );
        Assert.Equal(
            "Fake:ParentA:Certificate:ReloadSeconds",
            mappings["NINLIL_FAKE_PARENT_A_CERTIFICATE_RELOAD_SECONDS"]
        );
    }

    [Fact]
    public void BuildMappings_ReusesTheSameNestedTypeUnderTwoDifferentParents()
    {
        // TlsCertificateOptions is nested under four unrelated real parents in the real
        // application -- this confirms the recursion's own visited-type guard only blocks a
        // genuine cycle along one path, not legitimate reuse across sibling roots.
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings([
            new OptionsEnvironmentVariableRoot(typeof(FakeParentOptionsA)),
            new OptionsEnvironmentVariableRoot(typeof(FakeParentOptionsB)),
        ]);

        Assert.Equal(
            "Fake:ParentA:Certificate:PfxPath",
            mappings["NINLIL_FAKE_PARENT_A_CERTIFICATE_PFX_PATH"]
        );
        Assert.Equal(
            "Fake:ParentB:Certificate:PfxPath",
            mappings["NINLIL_FAKE_PARENT_B_CERTIFICATE_PFX_PATH"]
        );
    }

    [Fact]
    public void BuildMappings_SkipsAListValuedProperty()
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings([
            new OptionsEnvironmentVariableRoot(typeof(FakeParentOptionsA)),
        ]);

        Assert.DoesNotContain("NINLIL_FAKE_PARENT_A_KNOWN_PROXIES", mappings.Keys);
    }

    [Fact]
    public void BuildMappings_HonorsAnExplicitSectionNameOverride()
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings([
            new OptionsEnvironmentVariableRoot(
                typeof(FakeOptionsWithNoSectionNameConstant),
                "Fake:Overridden"
            ),
        ]);

        Assert.Equal(
            "Fake:Overridden:DisplayName",
            mappings["NINLIL_FAKE_OVERRIDDEN_DISPLAY_NAME"]
        );
    }

    [Fact]
    public void BuildMappings_ThrowsWithoutASectionNameConstantOrOverride()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OptionsEnvironmentVariableMap.BuildMappings([
                new OptionsEnvironmentVariableRoot(typeof(FakeOptionsWithNoSectionNameConstant)),
            ])
        );
    }

    [Fact]
    public void BuildMappings_ThrowsOnAGenuineNameCollision()
    {
        // "Fake:Colliding:HttpPort:Value" and "Fake:Colliding:HttpPortValue" are two genuinely
        // different configuration paths that both generate NINLIL_FAKE_COLLIDING_HTTP_PORT_VALUE
        // -- exactly the kind of ambiguity that must fail loudly at mapping-build time rather than
        // silently letting one setting shadow the other.
        Assert.Throws<InvalidOperationException>(() =>
            OptionsEnvironmentVariableMap.BuildMappings([
                new OptionsEnvironmentVariableRoot(typeof(FakeCollidingOptionsOne)),
                new OptionsEnvironmentVariableRoot(typeof(FakeCollidingOptionsTwo)),
            ])
        );
    }
}
