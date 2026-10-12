namespace FlexiFit.Api.Dtos
{
    public class ProgressTrackerDto
    {
        // Weight Metrics
        public double CurrentWeight { get; set; }
        public double WeightChange { get; set; }
        public List<ChartEntryDto> WeightHistory { get; set; } = new();

        // Workout Metrics
        public int CurrentStreak { get; set; }
        public string ComplianceSessions { get; set; } = "";
        public int TotalWorkoutDays { get; set; }           // Total scheduled workout days
        public int CompletedWorkoutDays { get; set; }       // Total actually completed
        public double WorkoutCompletionRate { get; set; }   // Percentage (0-100)

        // Nutrition Metrics
        public int MealsCompleted { get; set; }
        public int TotalMeals { get; set; }
        public double AvgWaterIntake { get; set; }

        // Advanced Nutrition Stats (Averages over the period)
        public double AverageTargetCalories { get; set; }   // Includes Base + Burn
        public double AverageConsumedCalories { get; set; }
        public double AverageBurnedCalories { get; set; }
        public double AverageNetCalories { get; set; }      // Consumed - Burned
        
        // Nutrition Compliance
        public int TotalDaysLogged { get; set; }
        public int DaysGoalMet { get; set; }
        public double NutritionAdherencePercentage { get; set; } // (Total Consumed / Total Target) * 100
        public double AverageCalorieDifference { get; set; }     // Average of (Target - Consumed) per day

        // Chart Data
        public double CompliancePercentage { get; set; }
        public int AvgCalories { get; set; }
        public List<ChartEntryDto> CalorieHistory { get; set; } = new();
    }

    public class ChartEntryDto
    {
        public string Label { get; set; } = "";
        public float Value { get; set; }
    }
}