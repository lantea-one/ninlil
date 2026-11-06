using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

// Define our namespace.
namespace Ninlil.Web.ModelBinding;

/// <summary>
///     This model binder reads one enum-typed query-string or route value by declared member name
///     only, ignoring case and surrounding whitespace. MVC's built-in enum binder also takes digit
///     strings, including numbers that name no member, so a caller could send
///     <c>?sort=7</c> and reach the action with an undefined value. An <c>[EnumMember]</c> wire
///     name is accepted as well, the same set the request-body converters take.
///     <see cref="EnumNameModelBinderProvider" /> hands this binder to every enum parameter.
///     <para>
///         A missing or blank value leaves the parameter unbound, so a nullable parameter stays
///         null and a non-nullable one keeps its default, exactly as before. A refused value adds
///         a model-state error that lists the valid names, which <c>[ApiController]</c> turns into
///         a 400 response.
///     </para>
/// </summary>
/// <param name="enumType">The enum type, with any <see cref="Nullable{T}" /> already removed.</param>
public sealed class EnumNameModelBinder(System.Type enumType) : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        ValueProviderResult result = bindingContext.ValueProvider.GetValue(
            bindingContext.ModelName
        );
        if (result == ValueProviderResult.None)
            return Task.CompletedTask;

        string text = result.FirstValue?.Trim();
        if (string.IsNullOrEmpty(text))
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);

        if (TryParse(text, out object value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.TryAddModelError(
            bindingContext.ModelName,
            $"'{text}' is not a valid value for {enumType.Name}. Valid values: "
                + string.Join(", ", Enum.GetNames(enumType))
                + "."
        );
        bindingContext.Result = ModelBindingResult.Failed();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Matches the text against the member names, then the wire names. A digit string matches
    ///     neither because member names cannot start with a digit and no wire name is a bare number.
    /// </summary>
    private bool TryParse(string text, out object value)
    {
        foreach (string name in Enum.GetNames(enumType))
        {
            if (!string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                continue;

            value = Enum.Parse(enumType, name);
            return true;
        }

        foreach (FieldInfo field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string wireName = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;
            if (
                wireName is null
                || !string.Equals(wireName, text, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            value = field.GetValue(null)!;
            return true;
        }

        value = null;
        return false;
    }
}
