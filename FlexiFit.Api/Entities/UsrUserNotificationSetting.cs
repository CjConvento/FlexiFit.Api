using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserNotificationSetting
{
    public int UserId { get; set; }

    public bool WorkoutReminderEnabled { get; set; }

    public TimeOnly? WorkoutReminderTime { get; set; }

    public bool MealReminderEnabled { get; set; }

    public TimeOnly? MealReminderTime { get; set; }

    public bool WaterReminderEnabled { get; set; }

    public TimeOnly? WaterStartTime { get; set; }

    public TimeOnly? WaterEndTime { get; set; }

    public int? WaterIntervalMinutes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int DailyWaterGoal { get; set; }

    public int GlassSizeMl { get; set; }

    public string CalorieDisplayMode { get; set; } = null!;

    public virtual UsrUser User { get; set; } = null!;
}
