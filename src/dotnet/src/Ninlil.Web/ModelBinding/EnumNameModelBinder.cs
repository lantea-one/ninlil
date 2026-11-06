using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Ninlil.Web.Serialization;

// Define our namespace.
namespace Ninlil.Web.ModelBinding;

/// <summary>
///     This model binder reads one enum-typed query-string or route value by declared name only,
///     ignoring case and surrounding whitespace. MVC's built-in enum binder also takes digit
///     strings, including numbers that name no member, so a caller could send
///     <c>?sort=7</c> and reach the action with an undefined value. The binder matches through
///     <see cref="RequestEnumNames{TEnum}" />, the same table the request-body converter
///     <see cref="RequestEnumNameJsonConverter{TEnum}" /> uses, so a body and a query accept the
///     same spellings (member names, <c>JsonStringEnumMemberName</c>, <c>EnumMember</c>,
///     <c>DisplayName</c> and written names) and a 400 lists the same valid names.
///     <see cref="EnumNameModelBinderProvider" /> hands this binder to every enum parameter.
///     <para>
///         A missing or blank value leaves the parameter unbound, so a nullable parameter stays
///         null and a non-nullable one keeps its default. A refused value adds a model-state error
///         that lists the valid names, which <c>[ApiController]</c> turns into a 400 response. A
///         parameter sent more than once is checked value by value: one refused value gives a 400,
///         otherwise the first value binds.
///     </para>
/// </summary>
public sealed class EnumNameModelBinder : IModelBinder
{
    private static readonly ConcurrentDictionary<System.Type, IRequestEnumNames> NamesByType =
        new();

    private readonly System.Type enumType;
    private readonly IRequestEnumNames names;

    /// <summary>Creates the binder for one enum type.</summary>
    /// <param name="enumType">The enum type, with any <see cref="Nullable{T}" /> already removed.</param>
    public EnumNameModelBinder(System.Type enumType)
    {
        this.enumType = enumType;
        names = NamesByType.GetOrAdd(
            enumType,
            type =>
                (IRequestEnumNames)
                    Activator.CreateInstance(typeof(RequestEnumNames<>).MakeGenericType(type))
        );
    }

    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        ValueProviderResult result = bindingContext.ValueProvider.GetValue(
            bindingContext.ModelName
        );
        if (result == ValueProviderResult.None)
            return Task.CompletedTask;

        // A repeated parameter must not smuggle a bad value behind a good first one.
        object first = null;
        bool hasFirst = false;
        for (int index = 0; index < result.Values.Count; index++)
        {
            string text = result.Values[index]?.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            if (!names.TryParse(text, out object value))
            {
                bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
                bindingContext.ModelState.TryAddModelError(
                    bindingContext.ModelName,
                    $"'{text}' is not a valid value for {enumType.Name}. Valid values: "
                        + names.ValidNames
                        + "."
                );
                bindingContext.Result = ModelBindingResult.Failed();
                return Task.CompletedTask;
            }

            if (index == 0)
            {
                first = value;
                hasFirst = true;
            }
        }

        // A blank value is not a value; only a non-blank first value binds. The model-state value
        // is set only then, because an entry for a blank value makes the request invalid.
        if (hasFirst)
        {
            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
            bindingContext.Result = ModelBindingResult.Success(first);
        }

        return Task.CompletedTask;
    }
}
