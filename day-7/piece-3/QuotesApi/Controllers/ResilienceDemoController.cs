using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Controllers;

// Manual smoke-test endpoint for the "my-service" resilient HttpClient.
// Each route forces a different failure mode so retry / timeout / circuit-breaker
// behaviour can be observed in the logs.
[ExcludeFromCodeCoverage]
[AllowAnonymous]
[ApiController]
[Route("api/resilience-demo")]
public class ResilienceDemoController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ResilienceDemoController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    // Hits httpbin.org/status/500 — server returns 500 every time, so the retry
    // strategy will exhaust its 3 attempts and the final 500 will surface.
    [HttpGet("retry")]
    public async Task<IActionResult> Retry(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("my-service");
        var response = await client.GetAsync("https://httpbin.org/status/500", cancellationToken);
        return StatusCode((int)response.StatusCode, new { upstreamStatus = (int)response.StatusCode });
    }

    // Hits httpbin.org/delay/30 — server holds the response for 30 s, well past
    // the 10 s total timeout, so the pipeline cancels the request.
    [HttpGet("timeout")]
    public async Task<IActionResult> Timeout(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("my-service");
        var response = await client.GetAsync("https://httpbin.org/delay/30", cancellationToken);
        return Ok(new { status = (int)response.StatusCode });
    }

    // Repeated 500s exceed the 50% failure ratio over the sampling window, which
    // opens the circuit; subsequent calls fail fast with BrokenCircuitException.
    [HttpGet("circuit")]
    public async Task<IActionResult> Circuit(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("my-service");
        var response = await client.GetAsync("https://httpbin.org/status/500", cancellationToken);
        return StatusCode((int)response.StatusCode, new { upstreamStatus = (int)response.StatusCode });
    }
}
