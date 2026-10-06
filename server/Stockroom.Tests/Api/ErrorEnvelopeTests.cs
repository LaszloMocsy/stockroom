using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Stockroom.Api.Errors;

namespace Stockroom.Tests.Api;

public sealed class ErrorEnvelopeTests : IAsyncLifetime
{
    private const string ExceptionText = "secret connection string in exception text";

    private TestApi _api = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _api = await TestApi.StartAsync(endpoints =>
    {
        endpoints.MapGet("/throws", string () => throw new InvalidOperationException(ExceptionText));
        endpoints.MapGet("/throws-after-start", async (HttpContext context) =>
        {
            await context.Response.WriteAsync("partial", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            throw new InvalidOperationException("failed mid-response");
        });
        endpoints.MapGet("/bad-request-exception", string () => throw new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge));
        endpoints.MapPost("/validated", (CreateThing thing) => TypedResults.Ok(thing));
        endpoints.MapGet("/validation-problem", () => TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["TargetQuantity"] = ["Must not be negative."],
            ["Lines[0].UnitCost"] = ["Is required."],
        }));
        endpoints.MapGet("/explicit-error", () => ApiResults.Error(StatusCodes.Status409Conflict, "insufficient_stock", "Only 2 units available.", new { Available = 2 }));
    });

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task UnhandledExceptionReturnsAGenericInternalError()
    {
        using var response = await _api.Client.GetAsync(new Uri("/throws", UriKind.Relative), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertEnvelope(body, "internal_error", "An unexpected error occurred.");
        Assert.DoesNotContain(ExceptionText, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledExceptionIsLoggedWithTheExceptionAndTheRequestAsFailed()
    {
        using var response = await _api.Client.GetAsync(new Uri("/throws", UriKind.Relative), Token);

        var entries = _api.Logs.Entries;
        Assert.Contains(entries, e => e.Level == LogLevel.Error && e.Exception?.Message == ExceptionText);
        Assert.Contains(entries, e => e.Category.EndsWith("RequestLoggingMiddleware", StringComparison.Ordinal)
            && e.Level == LogLevel.Error && e.Message.Contains("GET /throws responded 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExceptionAfterTheResponseStartedKeepsTheOriginalException()
    {
        // The status can no longer change, so the client sees a broken response; what matters is
        // that the original exception is what gets logged, not a secondary "response already started" error.
        try
        {
            using var response = await _api.Client.GetAsync(new Uri("/throws-after-start", UriKind.Relative), Token);
            await response.Content.ReadAsStringAsync(Token);
        }
        catch (HttpRequestException)
        {
        }
        catch (IOException)
        {
        }

        var entries = _api.Logs.Entries;
        Assert.Contains(entries, e => e.Exception?.Message == "failed mid-response");
        Assert.DoesNotContain(entries, e => e.Exception?.Message.Contains("already started", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(entries, e => e.Category.EndsWith("RequestLoggingMiddleware", StringComparison.Ordinal)
            && e.Message.Contains("GET /throws-after-start responded 200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BadHttpRequestExceptionKeepsItsStatus()
    {
        using var response = await _api.Client.GetAsync(new Uri("/bad-request-exception", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        AssertEnvelope(await response.Content.ReadAsStringAsync(Token), "payload_too_large");
    }

    [Fact]
    public async Task ValidationAttributesReturnValidationFailedWithSnakeCaseFields()
    {
        using var response = await _api.Client.PostAsJsonAsync("/validated", new { name = "", min_stock = -1 }, Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var details = AssertEnvelope(body, "validation_failed", "One or more fields are invalid.");
        var fields = details.GetProperty("fields");
        Assert.True(fields.TryGetProperty("name", out _), body);
        Assert.True(fields.TryGetProperty("min_stock", out var minStock), body);
        Assert.NotEmpty(minStock.EnumerateArray());
    }

    [Fact]
    public async Task ValidRequestBodyPassesValidation()
    {
        using var response = await _api.Client.PostAsJsonAsync("/validated", new { name = "Bolt", min_stock = 5 }, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidationProblemResultIsConvertedWithFieldNamesFollowingTheJsonPolicy()
    {
        using var response = await _api.Client.GetAsync(new Uri("/validation-problem", UriKind.Relative), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = AssertEnvelope(body, "validation_failed").GetProperty("fields");
        Assert.Equal("Must not be negative.", fields.GetProperty("target_quantity")[0].GetString());
        Assert.Equal("Is required.", fields.GetProperty("lines[0].unit_cost")[0].GetString());
    }

    [Fact]
    public async Task MalformedJsonReturnsBadRequest()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await _api.Client.PostAsync(new Uri("/validated", UriKind.Relative), content, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertEnvelope(await response.Content.ReadAsStringAsync(Token), "bad_request");
    }

    [Fact]
    public async Task WrongContentTypeReturnsUnsupportedMediaType()
    {
        using var content = new StringContent("name=x", Encoding.UTF8, "text/plain");

        using var response = await _api.Client.PostAsync(new Uri("/validated", UriKind.Relative), content, Token);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        AssertEnvelope(await response.Content.ReadAsStringAsync(Token), "unsupported_media_type");
    }

    [Fact]
    public async Task UnknownRouteReturnsNotFound()
    {
        using var response = await _api.Client.GetAsync(new Uri("/no-such-route", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertEnvelope(await response.Content.ReadAsStringAsync(Token), "not_found", "Not Found");
    }

    [Fact]
    public async Task WrongMethodReturnsMethodNotAllowed()
    {
        using var response = await _api.Client.DeleteAsync(new Uri("/validated", UriKind.Relative), Token);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        AssertEnvelope(await response.Content.ReadAsStringAsync(Token), "method_not_allowed");
    }

    [Fact]
    public async Task ExplicitErrorKeepsItsCodeMessageAndDetails()
    {
        using var response = await _api.Client.GetAsync(new Uri("/explicit-error", UriKind.Relative), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var details = AssertEnvelope(body, "insufficient_stock", "Only 2 units available.");
        Assert.Equal(2, details.GetProperty("available").GetInt32());
    }

    [Fact]
    public async Task EnvelopeIsJsonEvenWhenTheClientAsksForHtml()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/no-such-route");
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await _api.Client.SendAsync(request, Token);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertEnvelope(await response.Content.ReadAsStringAsync(Token), "not_found");
    }

    /// <summary>Asserts the body is exactly <c>{ "error": { "code", "message", "details" } }</c> and returns <c>details</c>.</summary>
    private static JsonElement AssertEnvelope(string body, string code, string? message = null)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(["error"], root.EnumerateObject().Select(p => p.Name));

        var error = root.GetProperty("error");
        Assert.Equal(["code", "message", "details"], error.EnumerateObject().Select(p => p.Name));
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()), body);
        if (message is not null)
        {
            Assert.Equal(message, error.GetProperty("message").GetString());
        }

        return error.GetProperty("details").Clone();
    }
}

/// <summary>Request body for the validation test endpoint.</summary>
public sealed record CreateThing([property: Required] string Name, [property: Range(0, int.MaxValue)] int MinStock);
