namespace FlexiFit.Api.Dtos
{
    public class BootstrapResponse
    {
        public bool ProfileComplete { get; set; }
        public int? UserId { get; set; }

        // Ito yung status summary para sa dashboard preview
        public BootstrapStatusDto? Status { get; set; }

        // Kung gusto mong ibalik yung saved data dati
        public object? ExistingProfile { get; set; }
    }

    public class BootstrapStatusDto
    {
        public string Code { get; set; } = "NEW"; // e.g., "ACTIVE", "INCOMPLETE"
        public bool HasActiveProgram { get; set; }
        public ActiveSessionDto? CurrentWorkoutSession { get; set; }
    }

    public class ActiveSessionDto
    {
        public int SessionId { get; set; }
        public int WorkoutDay { get; set; }
        public string Status { get; set; } = "PENDING";
        public int ExerciseCount { get; set; }
    }
}