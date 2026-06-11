using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace QuotesApi.Controllers;

// Smoke-test surface for the resilient "my-service" HttpClient.
//
// Every route makes a single OUTBOUND call through the Polly pipeline
// (bulkhead → retry → circuit breaker → timeout) to the local FlakyUpstream
// dependency. Toggle the dependency with POST /api/upstream/mode/fail|heal to
// drive the circuit breaker open → half-open → closed.
[ExcludeFromCodeCoverage]
[AllowAnonymous]
[ApiController]
[Route("api/resilience-demo")]
public class ResilienceDemoController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ResilienceDemoController> _logger;

    public ResilienceDemoController(
        IHttpClientFactory httpClientFactory,
        ILogger<ResilienceDemoController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // One resilient GET to the upstream /health endpoint. Result depends on the
    // upstream mode: 200 when healthy, retries+circuit behaviour when failing.
    // Used for the retry, circuit-open and recovery demos.
    [HttpGet("call")]
    public Task<IActionResult> Call(CancellationToken ct)
        => Invoke("/api/upstream/health", ct);

    // One resilient GET to the deliberately slow upstream endpoint. With the
    // default 30s server delay against the 10s per-attempt timeout, this drives
    // the timeout strategy. Also used (fired concurrently) for the bulkhead demo.
    [HttpGet("call-slow")]
    public Task<IActionResult> CallSlow([FromQuery] int ms = 30000, CancellationToken ct = default)
        => Invoke($"/api/upstream/slow?ms={ms}", ct);

    // Shared executor: makes the call and maps each Polly failure mode to a
    // distinct HTTP status so the response itself is part of the evidence.
    private async Task<IActionResult> Invoke(string path, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("my-service");
        try
        {
            var response = await client.GetAsync(path, ct);
            return StatusCode((int)response.StatusCode,
                new { upstreamStatus = (int)response.StatusCode, path });
        }
        catch (BrokenCircuitException ex)
        {
            // Circuit is OPEN — the call was short-circuited without touching upstream.
            _logger.LogWarning("[DEMO] call short-circuited (circuit open): {Message}", ex.Message);
            return StatusCode(503, new { error = "circuit-open", detail = ex.Message, path });
        }
        catch (TimeoutRejectedException ex)
        {
            _logger.LogWarning("[DEMO] call timed out: {Message}", ex.Message);
            return StatusCode(504, new { error = "timeout", detail = ex.Message, path });
        }
        catch (RateLimiterRejectedException ex)
        {
            // Bulkhead shed this call — concurrency limit was already saturated.
            _logger.LogWarning("[DEMO] call rejected by bulkhead: {Message}", ex.Message);
            return StatusCode(429, new { error = "bulkhead-rejected", detail = ex.Message, path });
        }
    }
}
