namespace FlexiFit.Api.Dtos
{
    public class CalendarHistoryDto
    {
        public int Day { get; set; }        // 1 to 28
        public int Week { get; set; }       // 1 to 4

        // WORKOUT & NUTRITION STATUS
        public string WorkoutStatus { get; set; } = "PENDING";   // PENDING, COMPLETED, SKIPPED, NOT STARTED
        public string NutritionStatus { get; set; } = "PENDING"; // PENDING, COMPLETED, SKIPPED, NOT STARTED

        // OVERALL STATUS (Ito yung "Master" color ng box sa Android)
        public string Status { get; set; } = "PENDING"; // PENDING, COMPLETED, SKIPPED, NOT STARTED

        public string? Summary { get; set; } // e.g., "Leg Day | 1800 kcal"
        public string DayType { get; set; }  // "WORKOUT" o "REST"
    }
}