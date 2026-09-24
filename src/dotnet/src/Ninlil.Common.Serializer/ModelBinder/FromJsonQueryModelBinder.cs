using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

// Define our namespace
namespace Ninlil.Common.Serializer.ModelBinder;

/// <summary>
///     This class maintains our model binder structure for JSON in a query parameter.
/// </summary>
/// <param name="validatorServiceProvider">The injected object model validation service provider instance.</param>
public class FromJsonQueryModelBinder(IObjectModelValidator validatorServiceProvider) : IModelBinder
{
    /// <summary>
    ///     This property contains the instance of our object model validation service provider.
    /// </summary>
    private readonly IObjectModelValidator _validator = validatorServiceProvider;

    /// <summary>
    ///     This method asynchronously binds the query parameter data to the model.
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
