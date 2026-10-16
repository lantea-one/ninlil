using Ninlil.Common.Extension.Tests.TestSupport;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     These tests call methods on <c>SampleCallableModel</c> through <see cref="CallTypeExtension" />.
///     Its overloads for 0 to 24 typed arguments all forward to two reflection paths (call on a given
///     instance, or call a static method or a new instance's method from the type), which are tested
///     here. The typed overloads are exercised with 0, 1, 2 and 5 arguments; the others forward the
///     same way.
/// </summary>
public class CallTypeExtensionTests
{
    [Fact]
    public void CallMethodOnInstanceInvokesInstanceMethod()
    {
        SampleCallableModel instance = new();

        object result = typeof(SampleCallableModel).CallMethod(
            instance,
            nameof(instance.InstanceGreet)
        );

        Assert.Equal("instance-hello", result);
    }

    [Fact]
    public void CallMethodOnInstanceWithArgumentsPassesThemThrough()
    {
        SampleCallableModel instance = new();

        object result = typeof(SampleCallableModel).CallMethod(
            instance,
            nameof(instance.Echo),
            "hi"
        );

        Assert.Equal("hi", result);
    }

    [Fact]
    public void CallMethodOnInstanceThrowsForUnknownMethodName()
    {
        SampleCallableModel instance = new();

        Assert.Throws<MissingMethodException>(() =>
            typeof(SampleCallableModel).CallMethod(instance, "DoesNotExist")
        );
    }

    [Fact]
    public void CallMethodGenericOnInstanceReturnsTypedResult()
    {
        SampleCallableModel instance = new();

        string result = typeof(SampleCallableModel).CallMethod<string>(
            instance,
            nameof(instance.Echo),
            "typed"
        );

        Assert.Equal("typed", result);
    }

    [Fact]
    public void CallMethodOnTypeDispatchesToStaticMethodWithoutInstantiating()
    {
        object result = typeof(SampleCallableModel).CallMethod(
            nameof(SampleCallableModel.StaticGreet)
        );

        Assert.Equal("static-hello", result);
    }

    [Fact]
    public void CallMethodOnTypeDispatchesToInstanceMethodByCreatingAnInstance()
    {
        SampleCallableModel probe = new();

        object result = typeof(SampleCallableModel).CallMethod(nameof(probe.InstanceGreet));

        Assert.Equal("instance-hello", result);
    }

    [Fact]
    public void CallMethodOnType_WithAWrongCaseStaticName_ThrowsMissingMethodException()
    {
        Assert.Throws<MissingMethodException>(() =>
            typeof(SampleCallableModel).CallMethod("staticgreet")
        );
    }

    [Fact]
    public void CallMethodOnType_WithAWrongCaseInstanceName_ThrowsMissingMethodException()
    {
        Assert.Throws<MissingMethodException>(() =>
            typeof(SampleCallableModel).CallMethod("instancegreet")
        );
    }

    [Fact]
    public void CallMethodOnType_WithAWrongCaseGenericName_ThrowsMissingMethodException()
    {
        Assert.Throws<MissingMethodException>(() =>
            typeof(SampleCallableModel).CallMethod("identity", [typeof(string)], "x")
        );
    }

    [Fact]
    public void CallMethodOnTypeThrowsForUnknownMethodName()
    {
        Assert.Throws<MissingMethodException>(() =>
            typeof(SampleCallableModel).CallMethod("DoesNotExist")
        );
    }

    [Fact]
    public void CallMethodGenericOnTypeWithGenericArgumentsInvokesGenericMethod()
    {
        SampleCallableModel probe = new();

        object result = typeof(SampleCallableModel).CallMethod(
            nameof(probe.Identity),
            [typeof(string)],
            "generic-value"
        );

        Assert.Equal("generic-value", result);
    }

    [Fact]
    public void CallMethodTypedWithTwoArgumentsPassesBothThrough()
    {
        SampleCallableModel probe = new();

        string result = typeof(SampleCallableModel).CallMethod<string, string, string>(
            nameof(probe.Concatenate),
            "foo",
            "bar"
        );

        Assert.Equal("foobar", result);
    }

    [Fact]
    public void CallMethodTypedWithFiveArgumentsPassesAllThrough()
    {
        SampleCallableModel probe = new();

        string result = typeof(SampleCallableModel).CallMethod<
            string,
            string,
            string,
            string,
            string,
            string
        >(nameof(probe.JoinFive), "a", "b", "c", "d", "e");

        Assert.Equal("a,b,c,d,e", result);
    }
}
