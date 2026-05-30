using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuotesApi.Entities;
using QuotesApi.Middleware;
using Xunit;

namespace Quotes.Tests.Unit.Middleware;

// ─────────────────────────────────────────────────────────────────────────────
//  ExceptionMiddleware unit tests
//
//  Verifies that all four branches behave correctly in isolation:
//    1. Happy path  — next() succeeds, response untouched
//    2. OperationCanceledException — 499 Client Closed Request (nginx convention)
//    3. DomainException — 400 Bad Request with ProblemDetails, title "Domain Rule Violation"
//    4. Unhandled Exception — 500 Internal Server Error with ProblemDetails
//
//  Uses a DefaultHttpContext with a real MemoryStream body so the middleware
//  can serialise ProblemDetails without needing a real ASP.NET Core pipeline.
// ─────────────────────────────────────────────────────────────────────────────

public class ExceptionMiddlewareTests
{
    private readonly ILogger<ExceptionMiddleware> _logger =
        Substitute.For<ILogger<ExceptionMiddleware>>();

    // Builds an HttpContext whose response body can be read back in assertions.
    private static DefaultHttpContext BuildContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    // Returns the serialised JSON body the middleware wrote to the response stream.
    private static async Task<JsonDocument> ReadResponseBodyAsync(DefaultHttpContext ctx)
    {
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        return await JsonDocument.ParseAsync(ctx.Response.Body);
    }

    // ── 1. Happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NoException_CallsNextAndLeavesResponseUntouched()
    {
        // Arrange
        var sut = new ExceptionMiddleware(
            next: _ => Task.CompletedTask,
            logger: _logger);
        var ctx = BuildContext();

        // Act
        await sut.InvokeAsync(ctx);

        // Assert
        ctx.Response.StatusCode.Should().Be(200, "the default status code must not be changed");
        ctx.Response.Body.Length.Should().Be(0, "no body is written on the happy path");
    }

    // ── 2. OperationCanceledException ───────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_OperationCanceledException_Returns499()
    {
        // Arrange
        var sut = new ExceptionMiddleware(
            next: _ => throw new OperationCanceledException("client disconnected"),
            logger: _logger);
        var ctx = BuildContext();

        // Act
        await sut.InvokeAsync(ctx);

        // Assert — 499 is the nginx "Client Closed Request" convention
        ctx.Response.StatusCode.Should().Be(499);
        ctx.Response.Body.Length.Should().Be(0,
            "no body should be written for client-cancel — the client is gone");
    }

    // ── 3. DomainException ──────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_DomainException_Returns400WithProblemDetails()
    {
        // Arrange
        const string violationMessage = "Collection name cannot be empty.";
        var sut = new ExceptionMiddleware(
            next: _ => throw new DomainException(violationMessage),
            logger: _logger);
        var ctx = BuildContext();

        // Act
        await sut.InvokeAsync(ctx);

        // Assert — status code
        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);

        // Assert — ProblemDetails body
        var body = await ReadResponseBodyAsync(ctx);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        body.RootElement.GetProperty("title").GetString().Should().Be("Domain Rule Violation");
        body.RootElement.GetProperty("detail").GetString().Should().Be(violationMessage);
    }

    [Fact]
    public async Task InvokeAsync_DomainException_ResponseBodyIsValidJson()
    {
        // Arrange
        var sut = new ExceptionMiddleware(
            next: _ => throw new DomainException("any violation"),
            logger: _logger);
        var ctx = BuildContext();

        // Act
        await sut.InvokeAsync(ctx);

        // Assert — body must be parseable as JSON (WriteAsJsonAsync is used)
        ctx.Response.Body.Length.Should().BeGreaterThan(0);
        var body = await ReadResponseBodyAsync(ctx);
        body.RootElement.TryGetProperty("title", out _).Should().BeTrue();
    }

    // ── 4. Unhandled Exception ─────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_UnhandledException_Returns500WithProblemDetails()
    {
        // Arrange
        const string unexpectedMessage = "Something went very wrong.";
        var sut = new ExceptionMiddleware(
            next: _ => throw new InvalidOperationException(unexpectedMessage),
            logger: _logger);
        var ctx = BuildContext();

        // Act
        await sut.InvokeAsync(ctx);

        // Assert — status code
        ctx.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);

        // Assert — ProblemDetails body
        var body = await ReadResponseBodyAsync(ctx);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        body.RootElement.GetProperty("title").GetString().Should().Be("Internal Server Error");
        body.RootElement.GetProperty("detail").GetString().Should().Be(unexpectedMessage);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_LogsError()
    {
        // Arrange
        var exception = new InvalidOperationException("boom");
        var sut = new ExceptionMiddleware(
            next: _ => throw exception,
            logger: _logger);
        var ctx = BuildContext();

        // Act
        await sut.InvokeAsync(ctx);

        // Assert — the middleware must log the exception at Error level
        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            exception,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
