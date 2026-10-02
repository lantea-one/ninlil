using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Web.Controllers;
using Ninlil.Web.ExceptionHandling;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="NinlilExceptionHandler" /> directly, without a host or an HTTP
///     round trip. The handler gets an <see cref="IProblemDetailsService" /> from a minimal
///     service collection with the <c>AddProblemDetails()</c> registration <c>Program.cs</c> also
///     makes, and writes to a <see cref="DefaultHttpContext" />. Each test checks how one
///     exception type is classified, including the fallback for an unexpected exception, which
///     must hide its message and return a correlation ID. The responses of endpoints such as
///     <see cref="UsersController" /> are covered by <see cref="AdministrativeErrorResponseTests" />
///     and <see cref="IdentityErrorResponseTests" />.
/// </summary>
public class NinlilExceptionHandlerTests
{
    /// <summary>
    ///     This method builds a <see cref="NinlilExceptionHandler" /> with an
    ///     <see cref="IProblemDetailsService" />, plus a <see cref="DefaultHttpContext" /> whose
    ///     response body is kept in memory so a test can read back what was written.
    /// </summary>
    private static (NinlilExceptionHandler Handler, DefaultHttpContext HttpContext) CreateHandler()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddProblemDetails();
        ServiceProvider provider = services.BuildServiceProvider();

        NinlilExceptionHandler handler = new(
            provider.GetRequiredService<IProblemDetailsService>(),
            NullLogger<NinlilExceptionHandler>.Instance
        );

        DefaultHttpContext httpContext = new() { RequestServices = provider };
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Headers.Accept = "application/json";
        httpContext.Response.Body = new MemoryStream();

        return (handler, httpContext);
    }

    /// <summary>
    ///     This method hands the given exception to a fresh <see cref="NinlilExceptionHandler" />
    ///     and returns the HTTP status code and the parsed JSON response body.
    /// </summary>
    private static async Task<(int StatusCode, JsonElement Body)> HandleAsync(Exception exception)
    {
        (NinlilExceptionHandler handler, DefaultHttpContext httpContext) = CreateHandler();

        bool handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);
        Assert.True(handled, "The handler must always handle every exception it is given.");

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using StreamReader reader = new(httpContext.Response.Body);
        string body = await reader.ReadToEndAsync();

        return (httpContext.Response.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
    }

    [Fact]
    public async Task NotFoundException_MapsTo404WithEntityTypeAndId()
    {
        (int statusCode, JsonElement body) = await HandleAsync(
            new NotFoundException("Zone", "abc-123")
        );

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.Equal(StatusCodes.Status404NotFound, body.GetProperty("status").GetInt32());
        Assert.Equal("Zone", body.GetProperty("entityType").GetString());
        Assert.Equal("abc-123", body.GetProperty("entityId").GetString());
        Assert.Contains("Zone", body.GetProperty("detail").GetString());
        Assert.Contains("abc-123", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ConflictException_MapsTo409WithItsOwnMessage()
    {
        const string message = "A zone named 'example.com' already exists.";

        (int statusCode, JsonElement body) = await HandleAsync(new ConflictException(message));

        Assert.Equal(StatusCodes.Status409Conflict, statusCode);
        Assert.Equal(StatusCodes.Status409Conflict, body.GetProperty("status").GetInt32());
        Assert.Equal(message, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ValidationException_WithASingleMessage_MapsTo400WithNoErrorsDictionary()
    {
        const string message = "The provided authenticator code is not valid.";

        (int statusCode, JsonElement body) = await HandleAsync(new ValidationException(message));

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.Equal(message, body.GetProperty("detail").GetString());
        Assert.False(body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task ValidationException_WithFieldErrors_MapsTo400WithAnErrorsDictionary()
    {
        Dictionary<string, string[]> errors = new()
        {
            ["DuplicateUserName"] = ["Username 'bob' is already taken."],
        };

        (int statusCode, JsonElement body) = await HandleAsync(new ValidationException(errors));

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        JsonElement errorsElement = body.GetProperty("errors");
        Assert.Equal(
            "Username 'bob' is already taken.",
            errorsElement.GetProperty("DuplicateUserName")[0].GetString()
        );
    }

    [Fact]
    public async Task NinlilException_MapsTo500WithItsOwnSafeMessage()
    {
        const string message = "The underlying signing key could not be generated.";

        (int statusCode, JsonElement body) = await HandleAsync(new NinlilException(message));

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);
        Assert.Equal(message, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnhandledException_MapsTo500WithACorrelationIdAndNoLeakedDetail()
    {
        const string secret = "Password=hunter2";
        InvalidOperationException exception = new(
            $"Failed to open connection: Data Source=/var/secret/db.sqlite3; {secret}"
        );

        (int statusCode, JsonElement body) = await HandleAsync(exception);

        Assert.Equal(StatusCodes.Status500InternalServerError, statusCode);

        string wholeBody = body.GetRawText();
        Assert.DoesNotContain(secret, wholeBody);
        Assert.DoesNotContain("db.sqlite3", wholeBody);
        Assert.DoesNotContain(exception.Message, wholeBody);

        Assert.True(body.TryGetProperty("correlationId", out JsonElement correlationId));
        Assert.False(string.IsNullOrWhiteSpace(correlationId.GetString()));
    }
}
