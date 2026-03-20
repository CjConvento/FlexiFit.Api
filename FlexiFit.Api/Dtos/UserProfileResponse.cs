namespace FlexiFit.Api.Dtos
{
    public class UserProfileResponse
    {
        public string Name { get; set; }
        public string Username { get; set; }

        public string AvatarUrl { get; set; } // <--- DAGDAG MO ITO

        public int Age { get; set; }
        public string Gender { get; set; }
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }
        public double BMI { get; set; }
        public string BmiCategory { get; set; }
        public int TotalSessions { get; set; } // SQL Count ng sessions
        public List<string> SelectedPrograms { get; set; } // List para sa Workout Data dialog
        public List<string> FitnessGoals { get; set; }    // List para sa Workout Data dialog
        public string GoalSubtitle { get; set; } = string.Empty;

        // IDAGDAG MO ITONG APAT:
        public int DailyCalorieTarget { get; set; }
        public double ProteinG { get; set; }
        public double CarbsG { get; set; }
        public double FatsG { get; set; }

        // --- DAGDAG MO ITONG DALAWANG LINE NA 'TO ---
        public int CompletedSessions { get; set; }
        public int TotalProgramSessions { get; set; }

        // --- ACHIEVEMENTS ---
        public List<string> UnlockedBadges { get; set; } = new List<string>();

        // --- ITO YUNG KULANG NA NAG-CA-CAUSE NG ERROR ---
        public int AchievementCount { get; set; }
        public List<string> UnlockedBadgeKeys { get; set; } = new List<string>();
        // ------------------------------------------------
    }
}
