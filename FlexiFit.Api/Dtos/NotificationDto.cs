// NotificationDto.cs
namespace FlexiFit.Api.Dtos;

public class NotificationSettingsDto
{
    public bool WorkoutReminderEnabled { get; set; }
    public string? WorkoutReminderTime { get; set; } // Format: "HH:mm"

    public bool MealReminderEnabled { get; set; }
    public string? MealReminderTime { get; set; } // Format: "HH:mm"

    public bool WaterReminderEnabled { get; set; }
    public string? WaterStartTime { get; set; } // Format: "HH:mm"
    public string? WaterEndTime { get; set; } // Format: "HH:mm"
    public int? WaterIntervalMinutes { get; set; }

    public int? DailyWaterGoal { get; set; } // in glasses or ml? (keep consistent)
    public int? GlassSizeMl { get; set; }
    public string? CalorieDisplayMode { get; set; } // "remaining" or "consumed"
}

public class NotificationHistoryDto  // <-- Renamed from NotificationItemDto para iwas confusion
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Time { get; set; } = "";
    public string Type { get; set; } = ""; // "WORKOUT", "MEAL", "WATER", "ACHIEVEMENT"
    public bool IsRead { get; set; } // Added this
}

public class UpdateNotificationSettingsRequest
{
    public NotificationSettingsDto Settings { get; set; } = new();
}