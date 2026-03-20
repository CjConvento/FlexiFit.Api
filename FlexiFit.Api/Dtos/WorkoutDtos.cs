namespace FlexiFit.Api.Dtos
{
    // 1. Main response para sa api/workout/today
    public class DailyWorkoutPlanDto
    {
        public int DayNo { get; set; }
        public string DayType { get; set; } = "";
        public string Message { get; set; } = "";
        public string Status { get; set; } = "";

        // Dagdag para sa Android Sync
        public int TotalDuration { get; set; }
        public int TotalCalories { get; set; }
        public string? FocusArea { get; set; }
        public string? Level { get; set; }

        // ✅ DAGDAG: Para sa skip functionality
        public bool CanSkip { get; set; } = true;
        public string? SkipMessage { get; set; }

        // ✅ DAGDAG: Para ma-track ang session ID
        public int SessionId { get; set; }

        public int TotalExercises => Warmups.Count + Workouts.Count;
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

        // ✅ FIXED: Status values: "COMPLETED", "SKIPPED"
        public string Status { get; set; } = "COMPLETED";

        // ✅ DAGDAG: Optional reason for skipping
        public string? SkipReason { get; set; }
    }

    // 5. ✅ BAGONG DTO: Response after completing/skipping
    public class WorkoutSessionResultDto
    {
        public string Message { get; set; } = "";
        public int CurrentDay { get; set; }
        public int NextDay { get; set; }
        public bool IsProgramFinished { get; set; }
        public string Status { get; set; } = "";
        public bool WasSkipped { get; set; }
        public string? SkipMessage { get; set; }
    }

    // 6. ✅ BAGONG DTO: Para sa workout history
    public class WorkoutHistoryDto
    {
        public int WorkoutDay { get; set; }
        public string Status { get; set; } = "";
        public DateTime? CompletedAt { get; set; }
        public DateTime? StartedAt { get; set; }
    }
}