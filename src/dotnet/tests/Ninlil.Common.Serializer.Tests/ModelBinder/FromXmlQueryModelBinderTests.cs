using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using Ninlil.Common.Serializer.ModelBinder;

namespace Ninlil.Common.Serializer.Tests.ModelBinder;

/// <summary>
///     This class contains unit tests for <see cref="FromXmlQueryModelBinder" />. See
///     <see cref="FromJsonQueryModelBinderTests" /> for the shared reasoning behind how the
///     binding context is constructed for these tests.
/// </summary>
public class FromXmlQueryModelBinderTests
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

    public class Simple
    {
        public string Value { get; set; }
    }

    private static ModelBindingContext BuildContext(
        string fieldName,
        string queryValue,
        Type modelType
    )
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
        ModelMetadata metadata = metadataProvider.GetMetadataForType(modelType);

        return DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadata,
            bindingInfo: null,
            modelName: fieldName
        );
    }

    [Fact]
    public async Task BindModelAsync_ValidXmlInQuery_BindsSuccessfully()
    {
        ModelBindingContext context = BuildContext(
            "model",
            Uri.EscapeDataString("<Simple><Value>x</Value></Simple>"),
            typeof(Simple)
        );

        await new FromXmlQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        var simple = Assert.IsType<Simple>(context.Result.Model);
        Assert.Equal("x", simple.Value);
    }

    [Fact]
    public async Task BindModelAsync_MissingQueryValue_LeavesResultUnset()
    {
        ModelBindingContext context = BuildContext("model", null, typeof(Simple));

        await new FromXmlQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
    }

    [Fact]
    public async Task BindModelAsync_MalformedXml_AddsModelError()
    {
        ModelBindingContext context = BuildContext(
            "model",
            Uri.EscapeDataString("not xml"),
            typeof(Simple)
        );

        await new FromXmlQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(context);

        Assert.False(context.ModelState.IsValid);
    }
}
