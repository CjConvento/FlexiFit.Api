namespace FlexiFit.Api.Dtos
{
    public class NotificationSettingsDto
    {
        public bool WorkoutReminderEnabled { get; set; }
        public string? WorkoutReminderTime { get; set; }

        public bool MealReminderEnabled { get; set; }
        public string? MealReminderTime { get; set; }

        public bool WaterReminderEnabled { get; set; }
        public string? WaterStartTime { get; set; }
        public string? WaterEndTime { get; set; }
        public int? WaterIntervalMinutes { get; set; }

        public int DailyWaterGoal { get; set; }
        public int GlassSizeMl { get; set; }
        public string? CalorieDisplayMode { get; set; }
    }
}