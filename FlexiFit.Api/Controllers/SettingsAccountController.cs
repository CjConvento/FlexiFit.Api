using System.Security.Claims;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/settings/account")]
    public class SettingsAccountController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;

        public SettingsAccountController(FlexiFitDbContext context)
        {
            _context = context;
        }

        [HttpPost("reset-progress")]
        public async Task<IActionResult> ResetProgress()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized();

            try
            {
                // 1. Reset Weight History (Mula sa UsrUserMetric)
                var weightLogs = await _context.UsrUserMetrics
                    .Where(x => x.UserId == userId).ToListAsync();
                _context.UsrUserMetrics.RemoveRange(weightLogs);

                // 2. Reset Daily Progress & Streak (Mula sa DailyProgressLog)
                var dailyLogs = await _context.DailyProgressLogs
                    .Where(x => x.UserId == userId).ToListAsync();
                _context.DailyProgressLogs.RemoveRange(dailyLogs);

                // 3. Reset Water Intake History (Mula sa NtrWaterLog)
                var waterLogs = await _context.NtrWaterLogs
                    .Where(x => x.UserId == userId).ToListAsync();
                _context.NtrWaterLogs.RemoveRange(waterLogs);

                // 4. Reset Workout Sessions (Mula sa UsrUserWorkoutSession)
                var workoutSessions = await _context.UsrUserWorkoutSessions
                    .Where(x => x.UserId == userId).ToListAsync();
                _context.UsrUserWorkoutSessions.RemoveRange(workoutSessions);

                // 5. Reset Achievements (Para fresh start talaga)
                var achievements = await _context.UsrUserGeneralAchievements
                    .Where(x => x.UserId == userId).ToListAsync();
                _context.UsrUserGeneralAchievements.RemoveRange(achievements);

                await _context.SaveChangesAsync();
                return Ok(new { message = "Your workouts, achievements, and nutrition and progress logs have been cleared." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error resetting progress: {ex.Message}");
            }
        }

        // PUT: api/settings/account/email (Para sa Manual Users)
        [HttpPut("email")]
        public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailDto dto)
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized();

            var user = await _context.UsrUsers.FindAsync(userId);
            if (user == null) return NotFound();

            user.Email = dto.NewEmail;
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PUT: api/settings/account/link-google (Para sa Google Users)
        [HttpPut("link-google")]
        public async Task<IActionResult> LinkGoogle([FromBody] UpdateGoogleEmailDto dto)
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized();

            var user = await _context.UsrUsers.FindAsync(userId);
            if (user == null) return NotFound();

            // Update credentials pero same UserId para keep ang data progress
            user.Email = dto.NewEmail;
            user.FirebaseUid = dto.NewFirebaseUid;
            user.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Successfully linked to new Google account." });
        }

        // DELETE: api/settings/account/terminate
        [HttpDelete("terminate")]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized();

            var user = await _context.UsrUsers.FindAsync(userId);
            if (user == null) return NotFound();

            _context.UsrUsers.Remove(user); // Cascade delete ang mag-lilinis sa data
            await _context.SaveChangesAsync();

            return Ok(new { message = "Account permanently deleted." });
        }

        private async Task<int?> GetCurrentUserIdAsync()
        {
            var firebaseUid = User.FindFirst("user_id")?.Value ?? User.FindFirst("uid")?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            UsrUser? user = null;
            if (!string.IsNullOrWhiteSpace(firebaseUid))
                user = await _context.UsrUsers.FirstOrDefaultAsync(x => x.FirebaseUid == firebaseUid);

            if (user == null && !string.IsNullOrWhiteSpace(email))
                user = await _context.UsrUsers.FirstOrDefaultAsync(x => x.Email == email);

            return user?.UserId;
        }

        // GET: api/settings/account/export
        [HttpGet("export")]
        public async Task<IActionResult> ExportData()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized();

            // Kunin lahat ng data mula sa iba't ibang tables
            var userData = new
            {
                Profile = await _context.UsrUsers
                    .Where(u => u.UserId == userId)
                    .Select(u => new { u.Username, u.Email, u.CreatedAt })
                    .FirstOrDefaultAsync(),

                Metrics = await _context.UsrUserMetrics
                    .Where(m => m.UserId == userId).ToListAsync(),

                WorkoutSessions = await _context.UsrUserWorkoutSessions
                    .Where(w => w.UserId == userId).ToListAsync(),

                WaterLogs = await _context.NtrWaterLogs
                    .Where(w => w.UserId == userId).ToListAsync(),

                DailyLogs = await _context.DailyProgressLogs
                    .Where(d => d.UserId == userId).ToListAsync(),

                Achievements = await _context.UsrUserGeneralAchievements
                    .Where(a => a.UserId == userId).ToListAsync()
            };

            return Ok(userData);
        }
    }
}