namespace FlexiFit.Api.Dtos
{
    public class ProgressTrackerDto
    {
        public double CompliancePercentage { get; set; }
        public string ComplianceSessions { get; set; } // e.g., "6 / 7 Sessions"
        public int AvgCalories { get; set; }
        public double AvgWaterIntake { get; set; }
        public int MealsCompleted { get; set; }
        public int TotalMeals { get; set; }
        public int CurrentStreak { get; set; }
        public double CurrentWeight { get; set; }
        public double WeightChange { get; set; }

        // Para sa mga Graphs
        public List<ChartEntryDto> WeightHistory { get; set; }
        public List<ChartEntryDto> CalorieHistory { get; set; }
    }

    public class ChartEntryDto
    {
        public string Label { get; set; } // e.g., "Mon", "W1"
        public float Value { get; set; }
    }
}
