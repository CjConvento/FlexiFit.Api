using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class NotificationService
{
    private readonly FlexiFitDbContext _context;

    public NotificationService(FlexiFitDbContext context)
    {
        _context = context;
    }

    public async Task<NotificationSettingsDto> GetSettingsAsync(int userId)
    {
        var settings = await _context.UsrUserNotificationSettings
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (settings == null)
        {
            // Return default values kung wala pang settings
            return new NotificationSettingsDto
            {
                WorkoutReminderEnabled = false,
                WaterIntervalMinutes = 60,
                DailyWaterGoal = 2000,      // 8 glasses of 250ml → 2000ml
                GlassSizeMl = 250,
                CalorieDisplayMode = "remaining"
            };
        }

        return MapToDto(settings);
    }

    public async Task UpdateSettingsAsync(int userId, NotificationSettingsDto dto)
    {
        var settings = await _context.UsrUserNotificationSettings
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (settings == null)
        {
            settings = new UsrUserNotificationSetting
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow
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
        
        // Use null coalescing to provide default values
        settings.WaterIntervalMinutes = dto.WaterIntervalMinutes ?? 60;
        settings.DailyWaterGoal = dto.DailyWaterGoal ?? 2000; // Aligned with default DTO
        settings.GlassSizeMl = dto.GlassSizeMl ?? 250;
        settings.CalorieDisplayMode = dto.CalorieDisplayMode ?? "remaining";
        settings.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<NotificationHistoryDto>> GetNotificationHistoryAsync(int userId)
    {
        return await _context.UsrNotificationHistories
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new NotificationHistoryDto
            {
                Id = x.Id,
                Title = x.Title,
                Message = x.Message,
                Type = x.Type,
                Time = x.CreatedAt.ToString("o")
            })
            .ToListAsync();
    }

    // ── Private Helpers ──────────────────────────────────────────
    private static TimeOnly? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return TimeOnly.TryParse(value, out var parsed) ? parsed : null;
    }

    private static NotificationSettingsDto MapToDto(UsrUserNotificationSetting s) => new()
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