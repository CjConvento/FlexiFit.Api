using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexiFit.API.Controllers
{
    // Tanging ang naka-login na Admin lamang ang pwedeng makapasok dito
    [Authorize(Roles = "ADMIN")] 
    [ApiController]
    [Route("api/dev")]
    public class DebugController : ControllerBase
    {
        private readonly IConfiguration _config;

        public DebugController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("scrape-token")]
        public IActionResult GetDevToken()
        {
            // Pagkuha ng token kung ito ay galing sa Authorization Header (Bearer Token)
            string? token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            // Kung gumagamit ka ng HttpOnly Cookies, dito naman ito kukunin:
            // Palitan ang "YourAuthCookieName" ng totoong pangalan ng cookie mo
            if (string.IsNullOrEmpty(token) && Request.Cookies.TryGetValue(".AspNetCore.Cookies", out var cookieToken))
            {
                token = cookieToken;
            }

            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new { message = "Naka-login ka pero hindi mahanap ang token sa request." });
            }

            return Ok(new { token  = token });
        }

        [HttpGet("appwrite-check")]
        public IActionResult CheckAppwrite()
        {
            var endpoint  = _config["StorageSettings:Appwrite:Endpoint"];
            var projectId = _config["StorageSettings:Appwrite:ProjectId"];
            var apiKey    = _config["StorageSettings:Appwrite:ApiKey"];

            return Ok(new
            {
                endpoint  = endpoint ?? "(MISSING)",
                projectId = projectId ?? "(MISSING)",
                hasApiKey = !string.IsNullOrEmpty(apiKey),
                apiKeyLength = apiKey?.Length ?? 0,
                allConfigKeys = _config.AsEnumerable()
                    .Where(kv => kv.Key.Contains("Appwrite", StringComparison.OrdinalIgnoreCase))
                    .Select(kv => kv.Key)
                    .ToList()
            });
        }
    }
}
