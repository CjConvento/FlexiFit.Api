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


        // New fields for Android hydration
        public string FitnessLifestyle { get; set; }
        public string FitnessLevel { get; set; }
        public List<string> Environment { get; set; } = new();
        public string BodyCompGoal { get; set; }
        public string DietaryType { get; set; }
        public bool UpperBodyInjury { get; set; }
        public bool LowerBodyInjury { get; set; }
        public bool JointProblems { get; set; }
        public bool ShortBreath { get; set; }
        public bool IsRehabUser { get; set; }
    }
}