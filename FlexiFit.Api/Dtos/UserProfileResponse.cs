namespace FlexiFit.Api.Dtos
{
    public class UserProfileResponse
    {
        public string Name { get; set; }
        public string Username { get; set; }
        public string AvatarUrl { get; set; }

        public int Age { get; set; }
        public string Gender { get; set; }
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }

        // DAGDAG: Target weight para sa Weight Quick Edit dialog
        public double TargetWeightKg { get; set; }

        public double BMI { get; set; }
        public string BmiCategory { get; set; }

        // DAGDAG: Nutrition goal para sa Nutritional Data dialog
        public string NutritionGoal { get; set; }

        public string GoalSubtitle { get; set; } = string.Empty;

        public int TotalSessions { get; set; }

        // DAGDAG: Total workouts (count from wrk_program_template_daytype_workouts)
        public int TotalWorkouts { get; set; }

        public int CompletedSessions { get; set; }
        public int TotalProgramSessions { get; set; }

        // Workout Data dialog
        public List<string> SelectedPrograms { get; set; } = new();
        public List<string> FitnessGoals { get; set; } = new();

        // Nutritional macros
        public int DailyCalorieTarget { get; set; }
        public double ProteinG { get; set; }
        public double CarbsG { get; set; }
        public double FatsG { get; set; }

        // Achievements
        public int AchievementCount { get; set; }
        public List<string> UnlockedBadges { get; set; } = new();
        public List<string> UnlockedBadgeKeys { get; set; } = new();
    }
}