using Microsoft.AspNetCore.Mvc;

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
            _logger.LogWarning(
                "Request cancelled by client. Path={RequestPath} StatusCode={StatusCode}",
                context.Request.Path,
                499);

            context.Response.StatusCode = 499; // Client Closed Request (nginx convention)
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(
                "Domain rule violation. ExceptionType={ExceptionType} Message={Message} Path={RequestPath} StatusCode={StatusCode}",
                ex.GetType().Name,
                ex.Message,
                context.Request.Path,
                400);

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
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception. ExceptionType={ExceptionType} Message={Message} Path={RequestPath} StatusCode={StatusCode}",
                ex.GetType().Name,
                ex.Message,
                context.Request.Path,
                500);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Detail = ex.Message
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }
}
