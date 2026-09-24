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
///     This class contains unit tests for <see cref="FromJsonQueryModelBinder" />, constructing a
///     real <see cref="ModelBindingContext" /> (via
///     <see cref="DefaultModelBindingContext.CreateBindingContext" />) backed by a real
///     <see cref="QueryStringValueProvider" /> reading from an in-memory query string, and a
///     no-op <see cref="IObjectModelValidator" /> stand-in (this project has no committed
///     integration-test host to exercise real model validation against).
/// </summary>
public class FromJsonQueryModelBinderTests
{
    /// <summary>
    ///     This no-op validator lets the binder's own <c>_validator.Validate(...)</c> call
    ///     complete without needing a fully wired-up DI-backed validation pipeline -- these tests
    ///     are only concerned with whether the binder itself parsed the query value correctly.
    /// </summary>
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
    public async Task BindModelAsync_ValidJsonInQuery_BindsSuccessfully()
    {
        ModelBindingContext context = BuildContext(
            "model",
            Uri.EscapeDataString("""{"Value":"x"}"""),
            typeof(Simple)
        );

        await new FromJsonQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        var simple = Assert.IsType<Simple>(context.Result.Model);
        Assert.Equal("x", simple.Value);
        Assert.True(context.ModelState.IsValid);
    }

    [Fact]
    public async Task BindModelAsync_MissingQueryValue_LeavesResultUnset()
    {
        ModelBindingContext context = BuildContext("model", null, typeof(Simple));

        await new FromJsonQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
    }

    [Fact]
    public async Task BindModelAsync_MalformedJson_AddsModelError()
    {
        ModelBindingContext context = BuildContext(
            "model",
            Uri.EscapeDataString("not json"),
            typeof(Simple)
        );

        await new FromJsonQueryModelBinder(new NoOpObjectModelValidator()).BindModelAsync(context);

        Assert.False(context.ModelState.IsValid);
        Assert.True(context.ModelState.ContainsKey("model"));
    }
}
