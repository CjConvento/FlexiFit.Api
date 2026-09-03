using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexiFit.API.Controllers
{
    // Tanging ang naka-login na Admin lamang ang pwedeng makapasok dito
    [Authorize(Roles = "Admin")] 
    [ApiController]
    [Route("api/dev")]
    public class DebugController : ControllerBase
    {
        [HttpGet("token")]
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

            return Ok(new { jwt = token });
        }
    }
}
