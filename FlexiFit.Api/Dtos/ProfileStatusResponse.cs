namespace FlexiFit.Api.Dtos
{
    public class DashboardResponseDto
    {
        public string Name { get; set; } = "";
        public string UserName { get; set; } = "";
        public string UserAvatar { get; set; } = ""; 
        public string? FitnessLevel { get; set; }
        public string? Goal { get; set; }
        public BmiDataDto? BmiData { get; set; }
        public NutritionDataDto? Nutrition { get; set; }

        // Para sa "Upcoming Workout" section
        public List<WorkoutExerciseDto>? UpcomingWorkouts { get; set; }

        // ETO ANG PINAKA-IMPORTANTE BABE: 
        // Ginawa nating List of Groups para gumana ang Spinner sa Android!
        public List<MealGroupDto>? TodayMeals { get; set; } 
    }

    public class BmiDataDto
    {
        public double Value { get; set; }
        public string? Status { get; set; } 
    }

    public class NutritionDataDto
    {
        public int TargetCalories { get; set; }
        public int Intake { get; set; }
        public double Burned { get; set; }
        public int NetCalories { get; set; } 
        public int Remaining { get; set; }
        public int WaterGlasses { get; set; }
        public int WaterTarget { get; set; } = 8;
    }
}