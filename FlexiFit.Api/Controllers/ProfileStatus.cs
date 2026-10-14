using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FlexiFit.Api.Services;
using Npgsql;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileStatusController : ControllerBase
    {
        private readonly ProfileService _profileService;

        public ProfileStatusController(
            ProfileService profileService)
        {
            _profileService = profileService;
        }

        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            // 1. Extract Claim
            var firebaseUid =
                User.FindFirst("user_id")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(firebaseUid))
                return Unauthorized(new { message = "Missing or invalid token claims." });

            // 2. Delegate to Service
            var status = await _profileService.GetProfileStatusAsync(firebaseUid, ct);

            // 3. Handle Result
            if (!status.UserExists)
                return NotFound(new { message = "User not found." });

            return Ok(new
            {
                firebaseUid,
                isProfileCompleted = status.IsProfileCompleted
            });
        }
    }
}