using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

// Define our namespace.
namespace Ninlil.Common.Serializer.ModelBinder;

/// <summary>
///     This MVC model binder reads a query-string parameter as an XML document, deserializes it into
///     the parameter's type with <see cref="XmlSerializer" /> and validates the result. Invalid XML
///     adds a model-state error. <see cref="Filter.FromXmlQueryOperationFilter" /> documents such
///     parameters in Swagger.
/// </summary>
/// <param name="validatorServiceProvider">The injected object model validation service provider instance.</param>
public class FromXmlQueryModelBinder(IObjectModelValidator validatorServiceProvider) : IModelBinder
{
    /// <summary>
    ///     This field contains the validator used on the deserialized model.
    /// </summary>
    private readonly IObjectModelValidator _validator = validatorServiceProvider;

    /// <summary>
    ///     This method binds the query parameter. An empty parameter leaves the binding result unset.
    /// </summary>
    /// <param name="bindingContext">The current context to bind to.</param>
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        // Localize the XML value from the binding context.
        string xml = bindingContext.ValueProvider.GetValue(bindingContext.FieldName).FirstValue;

        // Ensure we have an XML value to work with.
        if (xml?.Trim() is null or "")
            return Task.CompletedTask;

        // Try to deserialize the XML value.
        if (XmlSerializer.TryDeserialize(bindingContext.ModelType, xml, out object result))
        {
            // Reset the result into the binding context.
            bindingContext.Result = ModelBindingResult.Success(result);

            // Validate the model state.
            _validator.Validate(
                bindingContext.ActionContext,
                bindingContext.ValidationState,
                string.Empty,
                result
            );
        }
        // Otherwise, we were unable to deserialize the XML value.
        else
            bindingContext.ActionContext.ModelState.TryAddModelError(
                bindingContext.FieldName,
                new("Invalid XML"),
                bindingContext.ModelMetadata
            );

        // We're done, return the completed task.
        return Task.CompletedTask;
    }
}
