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
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;

        public NotificationsController(FlexiFitDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. SETTINGS ENDPOINTS
        // ==========================================

        [HttpGet("settings")]
        public async Task<ActionResult<NotificationSettingsDto>> GetSettings()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
                return Unauthorized(new { error = "Authenticated user not found." });

            var settings = await _context.UsrUserNotificationSettings
                .FirstOrDefaultAsync(x => x.UserId == userId.Value);

            // Kung wala pang settings, return default values gaya ng dati
            if (settings == null)
            {
                return Ok(new NotificationSettingsDto
                {
                    WorkoutReminderEnabled = false,
                    WaterIntervalMinutes = 60,
                    DailyWaterGoal = 8,
                    GlassSizeMl = 250,
                    CalorieDisplayMode = "remaining"
                });
            }

            return Ok(MapToDto(settings));
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] NotificationSettingsDto dto)
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
                return Unauthorized(new { error = "Authenticated user not found." });

            var settings = await _context.UsrUserNotificationSettings
                .FirstOrDefaultAsync(x => x.UserId == userId.Value);

            if (settings == null)
            {
                settings = new UsrUserNotificationSetting
                {
                    UserId = userId.Value,
                    CreatedAt = DateTime.Now
                };
                _context.UsrUserNotificationSettings.Add(settings);
            }

            // Map DTO to Entity
            settings.WorkoutReminderEnabled = dto.WorkoutReminderEnabled;
            settings.WorkoutReminderTime = ParseTime(dto.WorkoutReminderTime);
            settings.MealReminderEnabled = dto.MealReminderEnabled;
            settings.MealReminderTime = ParseTime(dto.MealReminderTime);
            settings.WaterReminderEnabled = dto.WaterReminderEnabled;
            settings.WaterStartTime = ParseTime(dto.WaterStartTime);
            settings.WaterEndTime = ParseTime(dto.WaterEndTime);
            settings.WaterIntervalMinutes = dto.WaterIntervalMinutes;
            settings.DailyWaterGoal = dto.DailyWaterGoal;
            settings.GlassSizeMl = dto.GlassSizeMl;
            settings.CalorieDisplayMode = dto.CalorieDisplayMode ?? "remaining";
            settings.UpdatedAt = DateTime.Now;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        // ==========================================
        // 2. HISTORY ENDPOINTS
        // ==========================================

        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<NotificationItem>>> GetNotificationHistory()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null) return Unauthorized();

            var history = await _context.UsrNotificationHistories
                .Where(x => x.UserId == userId.Value)
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new NotificationItem
                {
                    Id = x.Id,
                    Title = x.Title,
                    Message = x.Message,
                    Type = x.Type,
                    Time = x.CreatedAt.ToString("o")
                })
                .ToListAsync();

            return Ok(history);
        }

        // ==========================================
        // HELPERS (IBINALIK NATIN YUNG ORIGINAL LOGIC MO)
        // ==========================================

        private async Task<int?> GetCurrentUserIdAsync()
        {
            // Binabalik natin yung Firebase at Email checking logic mo
            var firebaseUid =
                User.FindFirst("user_id")?.Value ??
                User.FindFirst("uid")?.Value ??
                User.FindFirst("sub")?.Value;

            var email =
                User.FindFirst(ClaimTypes.Email)?.Value ??
                User.FindFirst("email")?.Value;

            UsrUser? user = null;

            if (!string.IsNullOrWhiteSpace(firebaseUid))
            {
                user = await _context.UsrUsers.FirstOrDefaultAsync(x => x.FirebaseUid == firebaseUid);
            }

            if (user == null && !string.IsNullOrWhiteSpace(email))
            {
                user = await _context.UsrUsers.FirstOrDefaultAsync(x => x.Email == email);
            }

            return user?.UserId;
        }

        private static TimeOnly? ParseTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return TimeOnly.TryParse(value, out var parsed) ? parsed : null;
        }

        private NotificationSettingsDto MapToDto(UsrUserNotificationSetting s) => new()
        {
            WorkoutReminderEnabled = s.WorkoutReminderEnabled,
            WorkoutReminderTime = s.WorkoutReminderTime?.ToString("HH:mm"),
            MealReminderEnabled = s.MealReminderEnabled,
            MealReminderTime = s.MealReminderTime?.ToString("HH:mm"),
            WaterReminderEnabled = s.WaterReminderEnabled,
            WaterStartTime = s.WaterStartTime?.ToString("HH:mm"),
            WaterEndTime = s.WaterEndTime?.ToString("HH:mm"),
            WaterIntervalMinutes = s.WaterIntervalMinutes,
            DailyWaterGoal = s.DailyWaterGoal,
            GlassSizeMl = s.GlassSizeMl,
            CalorieDisplayMode = s.CalorieDisplayMode
        };
    }
}