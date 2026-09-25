using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.ModelBinder;

/// <summary>
///     This MVC model binder reads a query-string parameter as a <c>data:</c> URI into a
///     <see cref="DataUriModel" /> and validates it. An invalid data URI adds a model-state error.
///     <see cref="Filter.FromDataUriQueryOperationFilter" /> documents such parameters in Swagger.
/// </summary>
/// <param name="validatorServiceProvider">The injected object model validation service provider instance.</param>
public class FromDataUriQueryModelBinder(IObjectModelValidator validatorServiceProvider)
    : IModelBinder
{
    /// <summary>
    ///     This field contains the validator used on the parsed model.
    /// </summary>
    private readonly IObjectModelValidator _validator = validatorServiceProvider;

    /// <summary>
    ///     This method binds the query parameter. An empty parameter leaves the binding result unset.
    /// </summary>
    /// <param name="bindingContext">The current context to bind to.</param>
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        // Localize the DataURI value from the binding context.
        string dataUri = bindingContext.ValueProvider.GetValue(bindingContext.FieldName).FirstValue;

        // Ensure we have a DataURI value to work with.
        if (dataUri?.Trim() is null or "")
            return Task.CompletedTask;

        // Try to parse the DataURI value.
        if (DataUriModel.TryParse(dataUri, out DataUriModel result))
        {
            // Reset the result into the binding context.
            bindingContext.Result = ModelBindingResult.Success(result);

            // Ensure we have a value then validate the model state.
            if (result is not null)
                _validator.Validate(
                    bindingContext.ActionContext,
                    bindingContext.ValidationState,
                    string.Empty,
                    result
                );
            // Otherwise, we were unable to deserialize the DataURI value.
            else
                bindingContext.ActionContext.ModelState.TryAddModelError(
                    bindingContext.FieldName,
                    new("Invalid DataURI"),
                    bindingContext.ModelMetadata
                );
        }
        // Otherwise, we were unable to deserialize the DataURI value.
        else
            bindingContext.ActionContext.ModelState.TryAddModelError(
                bindingContext.FieldName,
                new("Invalid DataURI"),
                bindingContext.ModelMetadata
            );

        // We're done, return the completed task.
        return Task.CompletedTask;
    }
}
