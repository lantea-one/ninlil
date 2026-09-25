using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Ninlil.Common.Serializer.Model;
using Ninlil.Common.Serializer.ModelBinder;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.ModelBinder;

/// <summary>
///     These tests bind data URI query values with <see cref="FromDataUriQueryModelBinder" />, using
///     a binding context built as in <see cref="FromJsonQueryModelBinderTests" />.
/// </summary>
public class FromDataUriQueryModelBinderTests
{
    private class NoOpObjectModelValidator : IObjectModelValidator
    {
        public void Validate(
            ActionContext actionContext,
            ValidationStateDictionary validationState,
            string prefix,
            object model
        ) { }
    }

    private static ModelBindingContext BuildContext(string fieldName, string queryValue)
    {
        var httpContext = new DefaultHttpContext();
        if (queryValue is not null)
            httpContext.Request.QueryString = new QueryString($"?{fieldName}={queryValue}");

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary()
        );

        var valueProvider = new QueryStringValueProvider(
            BindingSource.Query,
            httpContext.Request.Query,
            CultureInfo.InvariantCulture
        );

        var metadataProvider = new EmptyModelMetadataProvider();
        ModelMetadata metadata = metadataProvider.GetMetadataForType(typeof(DataUriModel));

        return DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadata,
            bindingInfo: null,
            modelName: fieldName
        );
    }

    [Fact]
    public async Task BindModelAsync_ValidDataUriInQuery_BindsSuccessfully()
    {
        ModelBindingContext context = BuildContext(
            "model",
            Uri.EscapeDataString("data:text/plain;base64,SGVsbG8=")
        );

        await new FromDataUriQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(
            context
        );

        Assert.True(context.Result.IsModelSet);
        var uri = Assert.IsType<DataUriModel>(context.Result.Model);
        Assert.Equal("text/plain", uri.MimeType);
    }

    [Fact]
    public async Task BindModelAsync_MissingQueryValue_LeavesResultUnset()
    {
        ModelBindingContext context = BuildContext("model", null);

        await new FromDataUriQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(
            context
        );

        Assert.False(context.Result.IsModelSet);
    }

    [Fact]
    public async Task BindModelAsync_MalformedDataUri_AddsModelError()
    {
        ModelBindingContext context = BuildContext("model", Uri.EscapeDataString("not-a-data-uri"));

        await new FromDataUriQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(
            context
        );

        Assert.False(context.ModelState.IsValid);
    }
}
