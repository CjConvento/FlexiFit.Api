using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/mobile")]
    public class MobileController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;

        public MobileController(FlexiFitDbContext context)
        {
            _context = context;
        }

        private string? GetFirebaseUid()
        {
            return User.FindFirst("user_id")?.Value
                ?? User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        [Authorize]
        [HttpPost("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var firebaseUid = GetFirebaseUid();
            if (string.IsNullOrWhiteSpace(firebaseUid))
                return Unauthorized(new { message = "invalid token: no uid claim" });

            // 1) Find user in SQL by firebase UID
            var user = await _context.UsrUsers
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

            // 2) OPTION A: auto-create if missing
            if (user == null)
            {
                user = new UsrUser
                {
                    FirebaseUid = firebaseUid,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.UsrUsers.Add(user);

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // If duplicate happened (rare), fetch again
                    user = await _context.UsrUsers
                        .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

                    if (user == null) throw;
                }
            }
            else
            {
                // Optional: update UpdatedAt on every login/bootstrap
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var userId = user.UserId;

            // 3) profileComplete based on profile version IsCurrent
            var profileComplete = await _context.UsrUserProfileVersions
                .AnyAsync(x => x.UserId == userId && x.IsCurrent);

            // 4) return bootstrap info
            return Ok(new
            {
                profileComplete = profileComplete,
                userId = userId
            });
        }
    }
}