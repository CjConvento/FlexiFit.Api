namespace FlexiFit.Api.Dtos
{
    // 1. Ang main response para sa api/workout/today
    public class DailyWorkoutPlanDto
    {
        public int DayNo { get; set; }
        public string DayType { get; set; } = "";
        public string Message { get; set; } = "";

        // Match sa WorkoutSessionResponse.kt
        public WorkoutProgramDto Program { get; set; } = new();
        public List<WorkoutExerciseDto> Warmups { get; set; } = new();
        public List<WorkoutExerciseDto> Workouts { get; set; } = new();
    }

    // 2. Para sa WorkoutProgram.kt
    public class WorkoutProgramDto
    {
        public int ProgramId { get; set; }
        public string ProgramName { get; set; } = "";
        public string Environment { get; set; } = "";
        public string Level { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public int Month { get; set; }
        public int Week { get; set; }
        public int Day { get; set; }
    }

    // 3. Para sa WorkoutItem.kt (Eto yung pinaka-importante babe)
    public class WorkoutExerciseDto
    {
        public int Id { get; set; } // Mag-ma-map sa 'val id' sa Kotlin
        public string Name { get; set; } = "";
        public string ImageFileName { get; set; } = ""; // Match sa 'val imageFileName'
        public string? MuscleGroup { get; set; }
        public int Sets { get; set; }
        public int Reps { get; set; }
        public int RestSeconds { get; set; }
        public int DurationMinutes { get; set; } = 5; // Galing sa table mo
        public int Calories { get; set; } // Galing sa calories_burned
        public string Description { get; set; } = ""; // Map sa Notes o Description
        public string? VideoUrl { get; set; }
        public decimal? LoadKg { get; set; } // For tracking progressive overload
    }

    // 4. Request DTO para sa pag-save ng session
    public class WorkoutSessionCompleteDto
    {
        public int TotalCalories { get; set; }
        public int TotalMinutes { get; set; }
    }
}