using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    [Authorize]
    public class ProfileStatusController : ControllerBase
    {
        private readonly FlexifitDbContext _context;

        public ProfileStatusController(FlexifitDbContext context)
        {
            _context = context;
        }

        [HttpGet("status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            // Firebase UID claim can vary depending on auth setup.
            // Try "user_id" first (common in Firebase), then fallback to NameIdentifier.
            var firebaseUid =
                User.FindFirst("user_id")?.Value ??
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(firebaseUid))
                return Unauthorized(new { message = "Missing or invalid token claims." });

            // Find local user mapped to Firebase UID
            var dbUser = await _context.UsrUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, ct);

            if (dbUser == null)
                return NotFound(new { message = "User not found." });

            // Profile completion check (current version exists)
            // NOTE: adjust dbUser.UserId vs dbUser.Id depending on your entity.
            var isProfileCompleted = await _context.UsrUserProfileVersions
                .AsNoTracking()
                .AnyAsync(p => p.UserId == dbUser.UserId && p.IsCurrent, ct);

            return Ok(new
            {
                firebaseUid,
                isProfileCompleted
            });
        }
    }
}