using Microsoft.AspNetCore.Mvc.ModelBinding;

// Define our namespace.
namespace Ninlil.Web.ModelBinding;

/// <summary>
///     This provider gives <see cref="EnumNameModelBinder" /> to every enum or nullable enum that
///     MVC binds from the query string or the route, whether it is an action parameter or a
///     property of a <c>[FromQuery]</c> model class. A future enum filter is therefore names-only
///     without extra code. Body-bound enums are not affected (they go through
///     <c>RequestEnumNameJsonConverterFactory</c>), nor are form and header values.
///     <c>Program.cs</c> inserts the provider at the front of the MVC binder providers, ahead of
///     MVC's own enum binder.
/// </summary>
public sealed class EnumNameModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        System.Type enumType = context.Metadata.UnderlyingOrModelType;
        if (!enumType.IsEnum)
            return null;

        // A null source is an unannotated parameter that [ApiController] infers from the query
        // or the route; a body, form or header source keeps its own binder.
        BindingSource source = context.BindingInfo?.BindingSource ?? context.Metadata.BindingSource;
        if (source is not null && source != BindingSource.Query && source != BindingSource.Path)
            return null;

        return new EnumNameModelBinder(enumType);
    }
}
