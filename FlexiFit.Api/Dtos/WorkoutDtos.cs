namespace FlexiFit.Api.Dtos
{
    // 1. Main response para sa api/workout/today
    public class DailyWorkoutPlanDto
    {
        public int DayNo { get; set; }
        public string DayType { get; set; } = "";
        public string Message { get; set; } = "";
        public int TotalExercises => Warmups.Count + Workouts.Count; // Helper para sa UI progress

        public WorkoutProgramDto Program { get; set; } = new();
        public List<WorkoutExerciseDto> Warmups { get; set; } = new();
        public List<WorkoutExerciseDto> Workouts { get; set; } = new();
    }

    // 2. Mirror ng WorkoutProgram.kt
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

    // 3. Mirror ng WorkoutItem.kt (Sync with Android)
    public class WorkoutExerciseDto
    {
        public int Id { get; set; } 
        public string Name { get; set; } = "";
        public string ImageFileName { get; set; } = ""; 
        public string? MuscleGroup { get; set; }
        public int Sets { get; set; }
        public int Reps { get; set; }
        public int RestSeconds { get; set; }
        public int DurationMinutes { get; set; }
        public int Calories { get; set; } 
        public string Description { get; set; } = ""; 
        public string? VideoUrl { get; set; }
        public decimal? LoadKg { get; set; } 
        
        // Dagdag para sa UI Logic sa Android
        public int Order { get; set; } 
        public bool IsCompleted { get; set; } 
    }

    // 4. Request DTO para sa pag-save ng session
    public class WorkoutSessionCompleteDto
    {
        public int SessionId { get; set; }
        public int TotalCalories { get; set; }
        public int TotalMinutes { get; set; }

        // Eto ang secret sauce para sa Skip at Rest Day logic mo
        // Halimbawa: "DONE", "SKIPPED", or "REST_COMPLETED"
        public string Status { get; set; } = "DONE";
    }
}