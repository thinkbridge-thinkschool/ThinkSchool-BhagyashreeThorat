using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    /// <summary>
    /// Protected endpoint that accepts both internal JWTs and Entra ID tokens.
    /// Useful for smoke-testing hybrid authentication.
    /// To test specifically with an Entra token, acquire one via the SPA OAuth flow
    /// and pass it as: Authorization: Bearer {entraToken}
    /// </summary>
    [Authorize]
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { message = "Authenticated with Entra ID" });
    }
}
