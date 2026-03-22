namespace FlexiFit.Api.Dtos
{
    public class ProgressTrackerDto
    {
        public double CompliancePercentage { get; set; }
        public string ComplianceSessions { get; set; } = "";
        public int AvgCalories { get; set; }
        public double AvgWaterIntake { get; set; }
        public int MealsCompleted { get; set; }
        public int TotalMeals { get; set; }
        public int CurrentStreak { get; set; }
        public double CurrentWeight { get; set; }
        public double WeightChange { get; set; }
        public List<ChartEntryDto> WeightHistory { get; set; } = new();
        public List<ChartEntryDto> CalorieHistory { get; set; } = new();
    }

    public class ChartEntryDto
    {
        public string Label { get; set; } = "";
        public float Value { get; set; }
    }
}