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
    [Route("api/settings/notifications")]
    public class NotificationSettingsController : ControllerBase
    {
        private readonly FlexifitDbContext _context;

        public NotificationSettingsController(FlexifitDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<NotificationSettingsDto>> Get()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
                return Unauthorized(new { error = "Authenticated user not found." });

            var settings = await _context.UsrUserNotificationSettings
                .FirstOrDefaultAsync(x => x.UserId == userId.Value);

            if (settings == null)
            {
                return Ok(new NotificationSettingsDto
                {
                    WorkoutReminderEnabled = false,
                    WorkoutReminderTime = null,

                    MealReminderEnabled = false,
                    MealReminderTime = null,

                    WaterReminderEnabled = false,
                    WaterStartTime = null,
                    WaterEndTime = null,
                    WaterIntervalMinutes = 60,

                    DailyWaterGoal = 8,
                    GlassSizeMl = 250,
                    CalorieDisplayMode = "remaining"
                });
            }

            return Ok(new NotificationSettingsDto
            {
                WorkoutReminderEnabled = settings.WorkoutReminderEnabled,
                WorkoutReminderTime = settings.WorkoutReminderTime?.ToString("HH:mm"),

                MealReminderEnabled = settings.MealReminderEnabled,
                MealReminderTime = settings.MealReminderTime?.ToString("HH:mm"),

                WaterReminderEnabled = settings.WaterReminderEnabled,
                WaterStartTime = settings.WaterStartTime?.ToString("HH:mm"),
                WaterEndTime = settings.WaterEndTime?.ToString("HH:mm"),
                WaterIntervalMinutes = settings.WaterIntervalMinutes,

                DailyWaterGoal = settings.DailyWaterGoal,
                GlassSizeMl = settings.GlassSizeMl,
                CalorieDisplayMode = settings.CalorieDisplayMode
            });
        }

        [HttpPut]
        public async Task<IActionResult> Put([FromBody] NotificationSettingsDto dto)
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

        private async Task<int?> GetCurrentUserIdAsync()
        {
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
                user = await _context.UsrUsers
                    .FirstOrDefaultAsync(x => x.FirebaseUid == firebaseUid);
            }

            if (user == null && !string.IsNullOrWhiteSpace(email))
            {
                user = await _context.UsrUsers
                    .FirstOrDefaultAsync(x => x.Email == email);
            }

            return user?.UserId;
        }

        private static TimeOnly? ParseTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return TimeOnly.TryParse(value, out var parsed) ? parsed : null;
        }
    }
}