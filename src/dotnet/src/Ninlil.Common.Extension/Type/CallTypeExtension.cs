using System.Reflection;

// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This class provides extension methods for calling methods on a type.
/// </summary>
public static class CallTypeExtension
{
    /// <summary>
    ///     This method throws an <see cref="ArgumentException" /> or an
    ///     <see cref="ArgumentNullException" />
    ///     when the <paramref name="type" /> is not a valid type.
    /// </summary>
    /// <param name="type">The type to validate.</param>
    /// <param name="allowStatic">
    ///     Optional <see cref="bool" /> denoting whether static methods are allowed or not.
    /// </param>
    /// <param name="argumentName">
    ///     Optional <see cref="string" /> denoting the name of the argument to
    ///     use in the exception which defaults to <paramref name="type" />'s name.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="type" /> is not a class, an interface, an abstract class,
    ///     or a static class when <paramref name="allowStatic" /> is false.
    /// </exception>
    private static void ThrowIfInvalid(
        System.Type type,
        bool allowStatic = true,
        string argumentName = null
    )
    {
        // Ensure the type is not null.
        ArgumentNullException.ThrowIfNull(type, argumentName?.Trim() ?? nameof(type));

        // Ensure the type is a class, not an interface nor abstract class.
        if (!type.IsClass)
            throw new ArgumentException(
                $"{type.FullName} is not a class.",
                argumentName?.Trim() ?? nameof(type)
            );

        // Ensure the type isn't an interface.
        if (type.IsInterface)
            throw new ArgumentException(
                $"{type.FullName} is an interface and cannot be instantiated for this method call.",
                argumentName?.Trim() ?? nameof(type)
            );

        // Ensure the type isn't an abstract.
        if (type.IsAbstract && !type.IsSealed)
            throw new ArgumentException(
                $"{type.FullName} is an abstract class and cannot be instantiated for this method call.",
                argumentName?.Trim() ?? nameof(type)
            );

        // Ensure the type isn't a static class if static methods are not allowed.
        if (!allowStatic && type.IsAbstract && type.IsSealed)
            throw new ArgumentException(
                $"{type.FullName} is a static class and cannot be instantiated for this method call.",
                argumentName?.Trim() ?? nameof(type)
            );
    }

    /// <summary>
    ///     This method allows you to call <paramref name="methodName" /> with
    ///     <paramref name="methodArguments" /> on <paramref name="instance" />.
    /// </summary>
    /// <param name="type">The instance bound to the extension method.</param>
    /// <param name="instance">The instance on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="instance" />.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <returns>
    ///     An <see cref="object" /> that is the result of the call to
    ///     <paramref name="methodName" /> on <paramref name="instance" />.
    /// </returns>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" />
    ///     does not exist on the <paramref name="instance" /> type.
    /// </exception>
    public static object CallMethod(
        this System.Type type,
        object instance,
        string methodName,
        params object[] methodArguments
    )
    {
        // Ensure the type is valid.
        ThrowIfInvalid(instance.GetType(), false, nameof(type));

        // Get the method info from the type.
        var methodInfo = instance
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        // Ensure the method exists.
        if (methodInfo is null)
            throw new MissingMethodException(type.FullName, methodName);

        // Invoke the method and return the result.
        return methodInfo.Invoke(instance, methodArguments.Length > 0 ? methodArguments : null);
    }

    /// <summary>
    ///     This method allows you to call generic <paramref name="methodName" /> with
    ///     <paramref name="methodArguments" /> on <paramref name="instance" /> using
    ///     the <paramref name="genericArguments" />.
    /// </summary>
    /// <param name="type">The instance bound to the extension method.</param>
    /// <param name="instance">The instance on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="instance" />.</param>
    /// <param name="genericArguments">The generic arguments to use for the method call.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <returns>
    ///     An <see cref="object" /> that is the result of the call to
    ///     <paramref name="methodName" /> on <paramref name="instance" />.
    /// </returns>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" />
    ///     does not exist on the <paramref name="instance" /> type.
    /// </exception>
    public static object CallMethod(
        this System.Type type,
        object instance,
        string methodName,
        IEnumerable<System.Type> genericArguments,
        params object[] methodArguments
    )
    {
        // Ensure the type is valid.
        ThrowIfInvalid(instance.GetType(), false, nameof(type));

        // Get the method info from the type.
        var methodInfo = instance
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        // Ensure the method exists.
        if (methodInfo is null)
            throw new MissingMethodException(type.FullName, methodName);

        // Make the method generic if generic arguments are provided.
        methodInfo = methodInfo.MakeGenericMethod(genericArguments.ToArray());

        // Ensure the method exists after making it generic.
        if (methodInfo is null)
            throw new MissingMethodException(type.FullName, methodName);

        // Invoke the method and return the result.
        return methodInfo.Invoke(instance, methodArguments.Length > 0 ? methodArguments : null);
    }

    /// <summary>
    ///     This method allows you to call <paramref name="methodName" /> with
    ///     <paramref name="methodArguments" /> on <paramref name="instance" />
    ///     with <typeparamref name="TResult" /> typed results.
    /// </summary>
    /// <param name="type">The instance bound to the extension method.</param>
    /// <param name="instance">The instance on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="instance" />.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <returns>
    ///     The <typeparamref name="TResult"/> results of the call to
    ///     <paramref name="methodName" /> on <paramref name="instance" />.
    /// </returns>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" />
    ///     does not exist on the <paramref name="instance" /> type.
    /// </exception>
    public static TResult CallMethod<TResult>(
        this System.Type type,
        object instance,
        string methodName,
        params object[] methodArguments
    ) => (TResult)CallMethod(type, instance, methodName, methodArguments);

    /// <summary>
    ///     This method allows you to call generic <paramref name="methodName" /> with
    ///     <paramref name="methodArguments" /> on <paramref name="instance" />
    ///     with <typeparamref name="TResult" /> typed results using the <paramref name="genericArguments" />.
    /// </summary>
    /// <param name="type">The instance bound to the extension method.</param>
    /// <param name="instance">The instance on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="instance" />.</param>
    /// <param name="genericArguments">The generic arguments to use for the method call.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <returns>
    ///     The <typeparamref name="TResult"/> results of the call to
    ///     <paramref name="methodName" /> on <paramref name="instance" />.
    /// </returns>
    /// <exception cref="MissingMethodException">
    ///     When the <paramref name="methodName" /> does not exist on the <paramref name="instance" /> type.
    /// </exception>
    public static TResult CallMethod<TResult>(
        this System.Type type,
        object instance,
        string methodName,
        IEnumerable<System.Type> genericArguments,
        params object[] methodArguments
    ) => (TResult)CallMethod(type, instance, methodName, genericArguments, methodArguments);

    /// <summary>
    ///     This extension method allows you to call <paramref name="methodName" />
    ///     with <paramref name="methodArguments" /> on a <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <returns>
    ///     An <see cref="object" /> that is the result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     When <paramref name="type" /> is null or <paramref name="methodName" /> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="type" /> is not a class or is an interface or abstract class.
    /// </exception>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" /> does not exist on the <paramref name="type" />.
    /// </exception>
    public static object CallMethod(
        this System.Type type,
        string methodName,
        params object[] methodArguments
    ) =>
        // Prefer a static method match first -- no instance was supplied to invoke against, so a
        // static method by this name can be called directly with no further work.
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            ? type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, methodArguments.Length > 0 ? methodArguments : null)
        // Otherwise, fall back to an instance method by creating a throwaway instance of the type
        // to invoke it against, since the caller only supplied a bare Type, not an instance, to call on.
        : type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            ? CallMethod(type, type.CreateInstance(), methodName, methodArguments)
        // Neither a static nor an instance method exists by this name; there's nothing left to call.
        : throw new MissingMethodException(type.FullName, methodName);

    /// <summary>
    ///     This extension method allows you to call a generic <paramref name="methodName" />
    ///     with <paramref name="methodArguments" /> on <paramref name="type" /> with
    ///     <paramref name="genericArguments" />/
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="genericArguments">The generic arguments to use for the method call.</param>
    /// <param name="methodArguments">The optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <returns>
    ///     An <see cref="object" /> that is the result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     When <paramref name="type" /> is null or <paramref name="methodName" /> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="type" /> is not a class or is an interface or abstract class.
    /// </exception>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" /> does not exist on the <paramref name="type" />.
    /// </exception>
    public static object CallMethod(
        this System.Type type,
        string methodName,
        IEnumerable<System.Type> genericArguments,
        params object[] methodArguments
    ) =>
        // Prefer a static method match first -- no instance was supplied to invoke against, so a
        // static generic method by this name can be closed and called directly with no further work.
        type.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Any(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            ? type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
                .MakeGenericMethod(genericArguments?.ToArray() ?? [])
                .Invoke(null, methodArguments.Length > 0 ? methodArguments : null)
        // Otherwise, fall back to an instance method by creating a throwaway instance of the type
        // to invoke it against, since the caller only supplied a bare Type, not an instance, to call on.
        : type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Any(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            ? CallMethod(type, type.CreateInstance(), methodName, genericArguments, methodArguments)
        // Neither a static nor an instance method exists by this name; there's nothing left to call.
        : throw new MissingMethodException(type.FullName, methodName);

    /// <summary>
    ///     This extension method allows you to call <paramref name="methodName" />
    ///     with <paramref name="methodArguments" /> on a <paramref name="type" />
    ///     with <typeparamref name="TResult" /> typed results.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <returns>
    ///     The <typeparamref name="TResult" /> typed results of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     When <paramref name="type" /> is null or <paramref name="methodName" /> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="type" /> is not a class or is an interface or abstract class.
    /// </exception>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" /> does not exist on the <paramref name="type" />.
    /// </exception>
    public static TResult CallMethod<TResult>(
        this System.Type type,
        string methodName,
        params object[] methodArguments
    ) => (TResult)CallMethod(type, methodName, methodArguments);

    /// <summary>
    ///     This extension method allows you to call generic <paramref name="methodName" />
    ///     with <paramref name="methodArguments" /> on a <paramref name="type" />
    ///     with <typeparamref name="TResult" /> typed results using the <paramref name="genericArguments" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="genericArguments">The generic arguments to use for the method call.</param>
    /// <param name="methodArguments">Optional arguments to pass to the <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <returns>
    ///     The <typeparamref name="TResult" /> typed results of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     When <paramref name="type" /> is null or <paramref name="methodName" /> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="type" /> is not a class or is an interface or abstract class.
    /// </exception>
    /// <exception cref="MissingMethodException">
    ///     When the method specified by <paramref name="methodName" /> does not exist on the <paramref name="type" />.
    /// </exception>
    public static TResult CallMethod<TResult>(
        this System.Type type,
        string methodName,
        IEnumerable<System.Type> genericArguments,
        params object[] methodArguments
    ) => (TResult)CallMethod(type, methodName, genericArguments, methodArguments);

    /// <summary>
    ///     This method allows you to call <paramref name="methodName" />
    ///     on an instantiated <paramref name="type" /> with a single argument of type
    ///     <typeparamref name="TArgument" /> with <typeparamref name="TResult" />
    ///     typed results.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the instantiated <paramref name="type" />.</param>
    /// <param name="argument">
    ///     The <typeparamref name="TArgument"/> typed argument to pass to <paramref name="methodName" />.
    /// </param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument">
    ///     The type of the <paramref name="argument" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" /> with
    ///     the <typeparamref name="TArgument" /> typed <paramref name="argument" /> passed to it.
    /// </returns>
    public static TResult CallMethod<TResult, TArgument>(
        this System.Type type,
        string methodName,
        TArgument argument
    ) => CallMethod<TResult>(type, methodName, argument);

    /// <summary>
    ///     This method allows you to call <paramref name="methodName" /> on an
    ///     instantiated <paramref name="type" /> with multiple typed arguments
    ///     and <typeparamref name="TResult" /> typed results.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the instantiated <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">
    ///     The type of the result to return from the call to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<TResult, TArgument1, TArgument2>(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2
    ) => CallMethod<TResult>(type, methodName, argument1, argument2);

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<TResult, TArgument1, TArgument2, TArgument3>(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3
    ) => CallMethod<TResult>(type, methodName, argument1, argument2, argument3);

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<TResult, TArgument1, TArgument2, TArgument3, TArgument4>(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4
    ) => CallMethod<TResult>(type, methodName, argument1, argument2, argument3, argument4);

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument19">The nineteenth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument19">
    ///     The type of <paramref name="argument19" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18,
        TArgument19
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18,
        TArgument19 argument19
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18,
            argument19
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument19">The nineteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument20">The twentieth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument19">
    ///     The type of <paramref name="argument19" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument20">
    ///     The type of <paramref name="argument20" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18,
        TArgument19,
        TArgument20
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18,
        TArgument19 argument19,
        TArgument20 argument20
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18,
            argument19,
            argument20
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument19">The nineteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument20">The twentieth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument21">The twenty-first argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument19">
    ///     The type of <paramref name="argument19" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument20">
    ///     The type of <paramref name="argument20" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument21">
    ///     The type of <paramref name="argument21" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18,
        TArgument19,
        TArgument20,
        TArgument21
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18,
        TArgument19 argument19,
        TArgument20 argument20,
        TArgument21 argument21
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18,
            argument19,
            argument20,
            argument21
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument19">The nineteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument20">The twentieth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument21">The twenty-first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument22">The twenty-second argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument19">
    ///     The type of <paramref name="argument19" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument20">
    ///     The type of <paramref name="argument20" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument21">
    ///     The type of <paramref name="argument21" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument22">
    ///     The type of <paramref name="argument22" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18,
        TArgument19,
        TArgument20,
        TArgument21,
        TArgument22
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18,
        TArgument19 argument19,
        TArgument20 argument20,
        TArgument21 argument21,
        TArgument22 argument22
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18,
            argument19,
            argument20,
            argument21,
            argument22
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument19">The nineteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument20">The twentieth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument21">The twenty-first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument22">The twenty-second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument23">The twenty-third argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument19">
    ///     The type of <paramref name="argument19" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument20">
    ///     The type of <paramref name="argument20" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument21">
    ///     The type of <paramref name="argument21" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument22">
    ///     The type of <paramref name="argument22" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument23">
    ///     The type of <paramref name="argument23" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18,
        TArgument19,
        TArgument20,
        TArgument21,
        TArgument22,
        TArgument23
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18,
        TArgument19 argument19,
        TArgument20 argument20,
        TArgument21 argument21,
        TArgument22 argument22,
        TArgument23 argument23
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18,
            argument19,
            argument20,
            argument21,
            argument22,
            argument23
        );

    /// <summary>
    ///     This method allows you to call a method on an instantiated <paramref name="type" />
    ///     with typed arguments and return a result of type <typeparamref name="TResult" />.
    /// </summary>
    /// <param name="type">The type on which the <paramref name="methodName" /> will be called.</param>
    /// <param name="methodName">The name of the method to call on the <paramref name="type" />.</param>
    /// <param name="argument1">The first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument2">The second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument3">The third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument4">The fourth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument5">The fifth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument6">The sixth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument7">The seventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument8">The eighth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument9">The ninth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument10">The tenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument11">The eleventh argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument12">The twelfth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument13">The thirteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument14">The fourteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument15">The fifteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument16">The sixteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument17">The seventeenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument18">The eighteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument19">The nineteenth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument20">The twentieth argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument21">The twenty-first argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument22">The twenty-second argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument23">The twenty-third argument to pass to <paramref name="methodName" />.</param>
    /// <param name="argument24">The twenty-fourth argument to pass to <paramref name="methodName" />.</param>
    /// <typeparam name="TResult">The type of the result to return from the method call.</typeparam>
    /// <typeparam name="TArgument1">
    ///     The type of <paramref name="argument1" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument2">
    ///     The type of <paramref name="argument2" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument3">
    ///     The type of <paramref name="argument3" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument4">
    ///     The type of <paramref name="argument4" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument5">
    ///     The type of <paramref name="argument5" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument6">
    ///     The type of <paramref name="argument6" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument7">
    ///     The type of <paramref name="argument7" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument8">
    ///     The type of <paramref name="argument8" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument9">
    ///     The type of <paramref name="argument9" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument10">
    ///     The type of <paramref name="argument10" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument11">
    ///     The type of <paramref name="argument11" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument12">
    ///     The type of <paramref name="argument12" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument13">
    ///     The type of <paramref name="argument13" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument14">
    ///     The type of <paramref name="argument14" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument15">
    ///     The type of <paramref name="argument15" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument16">
    ///     The type of <paramref name="argument16" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument17">
    ///     The type of <paramref name="argument17" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument18">
    ///     The type of <paramref name="argument18" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument19">
    ///     The type of <paramref name="argument19" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument20">
    ///     The type of <paramref name="argument20" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument21">
    ///     The type of <paramref name="argument21" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument22">
    ///     The type of <paramref name="argument22" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument23">
    ///     The type of <paramref name="argument23" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <typeparam name="TArgument24">
    ///     The type of <paramref name="argument24" /> to pass to <paramref name="methodName" />.
    /// </typeparam>
    /// <returns>
    ///     A <typeparamref name="TResult" /> typed result of the call to
    ///     <paramref name="methodName" /> on the instantiated <paramref name="type" />
    ///     with the typed arguments passed to it.
    /// </returns>
    public static TResult CallMethod<
        TResult,
        TArgument1,
        TArgument2,
        TArgument3,
        TArgument4,
        TArgument5,
        TArgument6,
        TArgument7,
        TArgument8,
        TArgument9,
        TArgument10,
        TArgument11,
        TArgument12,
        TArgument13,
        TArgument14,
        TArgument15,
        TArgument16,
        TArgument17,
        TArgument18,
        TArgument19,
        TArgument20,
        TArgument21,
        TArgument22,
        TArgument23,
        TArgument24
    >(
        this System.Type type,
        string methodName,
        TArgument1 argument1,
        TArgument2 argument2,
        TArgument3 argument3,
        TArgument4 argument4,
        TArgument5 argument5,
        TArgument6 argument6,
        TArgument7 argument7,
        TArgument8 argument8,
        TArgument9 argument9,
        TArgument10 argument10,
        TArgument11 argument11,
        TArgument12 argument12,
        TArgument13 argument13,
        TArgument14 argument14,
        TArgument15 argument15,
        TArgument16 argument16,
        TArgument17 argument17,
        TArgument18 argument18,
        TArgument19 argument19,
        TArgument20 argument20,
        TArgument21 argument21,
        TArgument22 argument22,
        TArgument23 argument23,
        TArgument24 argument24
    ) =>
        CallMethod<TResult>(
            type,
            methodName,
            argument1,
            argument2,
            argument3,
            argument4,
            argument5,
            argument6,
            argument7,
            argument8,
            argument9,
            argument10,
            argument11,
            argument12,
            argument13,
            argument14,
            argument15,
            argument16,
            argument17,
            argument18,
            argument19,
            argument20,
            argument21,
            argument22,
            argument23,
            argument24
        );
}
