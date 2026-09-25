using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

// Define our namespace.
namespace Ninlil.Common.Serializer.ModelBinder;

/// <summary>
///     This MVC model binder reads a query-string parameter as a JSON document, deserializes it into
///     the parameter's type with <see cref="JsonSerializer" /> and validates the result. Invalid JSON
///     adds a model-state error. <see cref="Filter.FromJsonQueryOperationFilter" /> documents such
///     parameters in Swagger.
/// </summary>
/// <param name="validatorServiceProvider">The injected object model validation service provider instance.</param>
public class FromJsonQueryModelBinder(IObjectModelValidator validatorServiceProvider) : IModelBinder
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
        // Localize the JSON value from the binding context.
        string json = bindingContext.ValueProvider.GetValue(bindingContext.FieldName).FirstValue;

        // Ensure we have a JSON value to work with.
        if (json?.Trim() is null or "")
            return Task.CompletedTask;

        // Try to deserialize the JSON value.
        if (JsonSerializer.TryDeserialize(bindingContext.ModelType, json, out object result))
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
        // Otherwise, we were unable to deserialize the JSON value.
        else
            bindingContext.ActionContext.ModelState.TryAddModelError(
                bindingContext.FieldName,
                new("Invalid JSON"),
                bindingContext.ModelMetadata
            );

        // We're done, return the completed task.
        return Task.CompletedTask;
    }
}
