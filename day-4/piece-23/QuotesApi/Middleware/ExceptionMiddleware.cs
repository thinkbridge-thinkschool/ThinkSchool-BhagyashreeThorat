using Microsoft.AspNetCore.Mvc;
using QuotesApi.Entities;

namespace QuotesApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or cancelled the request before we finished
            _logger.LogWarning("Request was cancelled by the client");
            context.Response.StatusCode = 499; // Client Closed Request (nginx convention)
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = 400,
                Title = "Domain Rule Violation",
                Detail = ex.Message
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled exception occurred");

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = exception.Message
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }
}