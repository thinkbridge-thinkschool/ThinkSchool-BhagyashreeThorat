using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Controllers;

// A controllable stand-in for a real outbound dependency.
//
// The resilient "my-service" HttpClient calls these routes over localhost, so the
// whole resilience lab runs offline with no reliance on a flaky third-party host
// (the old demo hit httpbin.org). A single static flag lets us flip the dependency
// between "healthy" and "down" on demand, which is exactly what's needed to drive
// the circuit breaker from open → half-open → closed deterministically.
[ExcludeFromCodeCoverage]
[AllowAnonymous]
[ApiController]
[Route("api/upstream")]
public class FlakyUpstreamController : ControllerBase
{
    // Process-wide switch shared by every request. volatile because it's written
    // from the control routes and read from concurrent inbound calls.
    private static volatile bool _failing;

    private readonly ILogger<FlakyUpstreamController> _logger;

    public FlakyUpstreamController(ILogger<FlakyUpstreamController> logger)
    {
        _logger = logger;
    }

    // The endpoint the resilient client actually depends on. Returns 200 when the
    // dependency is "healthy" and 500 when we've flipped it to "down".
    [HttpGet("health")]
    public IActionResult Health()
    {
        if (_failing)
        {
            _logger.LogInformation("[UPSTREAM] returning 500 (failing mode ON)");
            return StatusCode(500, new { upstream = "down" });
        }

        return Ok(new { upstream = "healthy" });
    }

    // Sleeps for the requested duration so the caller's timeout / bulkhead can be
    // exercised. Defaults to 30s — longer than the 10s per-attempt timeout.
    [HttpGet("slow")]
    public async Task<IActionResult> Slow([FromQuery] int ms = 30000, CancellationToken ct = default)
    {
        _logger.LogInformation("[UPSTREAM] slow response, sleeping {DelayMs}ms", ms);
        await Task.Delay(ms, ct);
        return Ok(new { upstream = "healthy-but-slow", sleptMs = ms });
    }

    // Control plane: flip the dependency down / back up.
    [HttpPost("mode/fail")]
    public IActionResult Fail()
    {
        _failing = true;
        _logger.LogWarning("[UPSTREAM] mode -> FAILING (will return 500)");
        return Ok(new { failing = true });
    }

    [HttpPost("mode/heal")]
    public IActionResult Heal()
    {
        _failing = false;
        _logger.LogWarning("[UPSTREAM] mode -> HEALTHY (will return 200)");
        return Ok(new { failing = false });
    }

    [HttpGet("mode")]
    public IActionResult Mode() => Ok(new { failing = _failing });
}
