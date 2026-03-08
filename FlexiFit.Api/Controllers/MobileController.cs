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
            return User.FindFirst("firebase_uid")?.Value;
        }

        private int? GetUserId()
        {
            var raw = User.FindFirst("user_id")?.Value;

            return int.TryParse(raw, out var id) ? id : null;
        }

        [Authorize]
        [HttpPost("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var firebaseUid = GetFirebaseUid();
            if (string.IsNullOrWhiteSpace(firebaseUid))
            {
                return Unauthorized(new { message = "invalid token: no firebase_uid claim" });
            }

            var user = await _context.UsrUsers
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

            if (user == null)
            {
                return Unauthorized(new { message = "user not found" });
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var profileComplete = await _context.UsrUserProfileVersions
                .AnyAsync(x => x.UserId == user.UserId && x.IsCurrent);

            return Ok(new
            {
                profileComplete,
                userId = user.UserId
            });
        }
    }
}