using Microsoft.AspNetCore.Mvc;

namespace Agora.Api.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new HealthResponse("healthy", "agora-net", DateTimeOffset.UtcNow));
}

public record HealthResponse(string Status, string Service, DateTimeOffset UtcNow);
