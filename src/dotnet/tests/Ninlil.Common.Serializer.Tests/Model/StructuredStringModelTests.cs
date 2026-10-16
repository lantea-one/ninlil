using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     These tests cover the behaviour <see cref="StructuredStringModel" /> adds on top of its
///     subclasses: the static <c>IsMatch</c> check, equality against null, the message of a rejected
///     value and the XML round trip through <c>ReadXml</c> and <c>WriteXml</c>. They use
///     <see cref="NetworkAddressModel" /> as the concrete model and a private holder type for the XML
///     tests, with no shared state. Tests marked "fails today" threw or failed before the fix.
/// </summary>
public class StructuredStringModelTests
{
    /// <summary>
    ///     This type is the XML payload of the round-trip tests: a structured-string property followed
    ///     by a plain one, so a reader left at the wrong position loses <see cref="After" />.
    /// </summary>
    public class XmlProbeHolder
    {
        /// <summary>
        ///     This property holds the structured-string value written as the element's text.
        /// </summary>
        public NetworkAddressModel Address { get; set; }

        /// <summary>
        ///     This property is read after <see cref="Address" /> and proves the reader position.
        /// </summary>
        public string After { get; set; }
    }

    // Fails today: IsMatch threw MissingMethodException for every type.
    [Fact]
    public void IsMatch_ForAValidValue_ReturnsTrue()
    {
        Assert.True(StructuredStringModel.IsMatch(typeof(NetworkAddressModel), "10.0.0.0/24"));
        Assert.True(StructuredStringModel.IsMatch<NetworkAddressModel>("2001:db8::/32"));
    }

    // Fails today: IsMatch threw MissingMethodException for every type.
    [Theory]
    [InlineData("!!not-valid")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData(null)]
    public void IsMatch_ForAValueThatMatchesNoPattern_ReturnsFalse(string value)
    {
        Assert.False(StructuredStringModel.IsMatch(typeof(NetworkAddressModel), value));
        Assert.False(StructuredStringModel.IsMatch<NetworkAddressModel>(value));
    }

    [Fact]
    public void IsMatch_ForATypeThatIsNotAStructuredString_ReturnsFalse()
    {
        Assert.False(StructuredStringModel.IsMatch(typeof(string), "10.0.0.0/24"));
    }

    [Fact]
    public void IsMatch_ForTheAbstractBaseType_ReturnsFalse()
    {
        // The abstract base cannot be created, so it has no patterns to test.
        Assert.False(StructuredStringModel.IsMatch(typeof(StructuredStringModel), "10.0.0.0/24"));
    }

    // Fails today: two null operands compared as not equal.
    [Fact]
    public void EqualityOperator_WithTwoNullModels_IsTrue()
    {
        Assert.True((StructuredStringModel)null == (StructuredStringModel)null);
        Assert.False((StructuredStringModel)null != (StructuredStringModel)null);
    }

    // Fails today: null == model was false while model == null was true.
    [Fact]
    public void EqualityOperator_WithAnEmptyModelAndNull_IsTrueInBothOrders()
    {
        var empty = new NetworkAddressModel();

        Assert.True(empty == null);
        Assert.True(null == empty);
        Assert.False(empty != null);
        Assert.False(null != empty);
    }

    [Fact]
    public void EqualityOperator_WithAValidModelAndNull_IsFalseInBothOrders()
    {
        var valid = new NetworkAddressModel("10.0.0.0/24");

        Assert.False(valid == null);
        Assert.False(null == valid);
        Assert.True(valid != null);
        Assert.True(null != valid);
    }

    [Fact]
    public void EqualityOperator_WithTwoModels_ComparesTheirStringForms()
    {
        Assert.True(
            new NetworkAddressModel("10.0.0.0/24") == new NetworkAddressModel("10.0.0.0/24")
        );
        Assert.True(
            new NetworkAddressModel("10.0.0.0/24") != new NetworkAddressModel("10.0.0.0/25")
        );
    }

    // Fails today: the message named the model type where the value belongs.
    [Fact]
    public void Parse_ForARejectedValue_NamesTheValueAndTheModelType()
    {
        var error = Assert.Throws<FormatException>(() =>
            new NetworkAddressModel().Parse("garbage")
        );

        Assert.StartsWith("The provided value 'garbage' does not match", error.Message);
        Assert.Contains("for NetworkAddressModel", error.Message);
    }

    // Fails today: ReadXml threw MissingMethodException (wrapped in InvalidOperationException).
    [Fact]
    public void Xml_RoundTrip_KeepsTheAddressAndTheElementAfterIt()
    {
        var holder = new XmlProbeHolder
        {
            Address = new NetworkAddressModel("10.0.0.0/24"),
            After = "tail",
        };

        string xml = XmlSerializer.Serialize(holder);
        XmlProbeHolder result = XmlSerializer.Deserialize<XmlProbeHolder>(xml);

        Assert.Equal(holder.Address, result.Address);
        Assert.Equal("10.0.0.0/24", result.Address.ToString());
        Assert.Equal("tail", result.After);
    }

    // Fails today: ReadXml threw MissingMethodException (wrapped in InvalidOperationException).
    [Fact]
    public void Xml_RoundTrip_WithAnEmptyAddress_KeepsItEmptyAndKeepsTheElementAfterIt()
    {
        var holder = new XmlProbeHolder { Address = new NetworkAddressModel(), After = "tail" };

        string xml = XmlSerializer.Serialize(holder);
        XmlProbeHolder result = XmlSerializer.Deserialize<XmlProbeHolder>(xml);

        Assert.NotNull(result.Address);
        Assert.True(result.Address.IsNull());
        Assert.Equal("tail", result.After);
    }

    // Fails today: ReadXml threw MissingMethodException (wrapped in InvalidOperationException).
    [Fact]
    public void Xml_SelfClosingAddressElement_ReadsAsEmptyAndKeepsTheElementAfterIt()
    {
        XmlProbeHolder result = XmlSerializer.Deserialize<XmlProbeHolder>(
            "<XmlProbeHolder><Address /><After>tail</After></XmlProbeHolder>"
        );

        Assert.True(result.Address.IsNull());
        Assert.Equal("tail", result.After);
    }

    // Fails today: the inner exception was MissingMethodException, not FormatException.
    [Fact]
    public void Xml_AddressThatMatchesNoPattern_ThrowsAFormatExceptionNamingTheValue()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            XmlSerializer.Deserialize<XmlProbeHolder>(
                "<XmlProbeHolder><Address>garbage</Address><After>tail</After></XmlProbeHolder>"
            )
        );

        FormatException inner = Assert.IsType<FormatException>(error.InnerException);
        Assert.Contains("'garbage'", inner.Message);
    }

    // Fails today: the inner exception was MissingMethodException, not FormatException.
    [Fact]
    public void Xml_AddressThatMatchesButIsNotAnAddress_ThrowsAFormatException()
    {
        // The pattern accepts the characters, but IPAddress rejects the value, so parsing yields no model.
        var error = Assert.Throws<InvalidOperationException>(() =>
            XmlSerializer.Deserialize<XmlProbeHolder>(
                "<XmlProbeHolder><Address>999.1.1.1</Address><After>tail</After></XmlProbeHolder>"
            )
        );

        FormatException inner = Assert.IsType<FormatException>(error.InnerException);
        Assert.Contains("'999.1.1.1'", inner.Message);
    }
}
