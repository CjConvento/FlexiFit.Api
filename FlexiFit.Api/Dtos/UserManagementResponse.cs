namespace FlexiFit.Api.Dtos
{
    public class UserManagementResponse
    {
        public string Name { get; set; }
        public string Username { get; set; }
        public string Gender { get; set; }
        public int Age { get; set; }
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }
        public double Bmi { get; set; }
        public string BmiCategory { get; set; }
        public string GoalSubtitle { get; set; }
        public int CompletedSessions { get; set; }  // map to totalSessions in Android
        public int TotalProgramSessions { get; set; }
        public int AchievementCount { get; set; }
        public List<string> UnlockedBadgeKeys { get; set; }
        public int DailyCalorieTarget { get; set; }
        public double ProteinG { get; set; }
        public double CarbsG { get; set; }
        public double FatsG { get; set; }
    }
}
