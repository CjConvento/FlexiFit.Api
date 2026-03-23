namespace FlexiFit.Api.Dtos
{
    public class UpdateOnboardingRequest
    {
        // PG1 & 1.5

        public int Age { get; set; }
        public string Gender { get; set; }
        public double HeightCm { get; set; }
        public double WeightKg { get; set; }
        public double TargetWeightKg { get; set; }

        // PG2: Health Flags
        public bool UpperBodyInjury { get; set; }
        public bool LowerBodyInjury { get; set; }
        public bool JointProblems { get; set; }
        public bool ShortBreath { get; set; }

        // PG3 - PG7
        public string FitnessLifestyle { get; set; }
        public string FitnessLevel { get; set; }
        public string Environment { get; set; }
        public string BodyCompGoal { get; set; }
        public string DietaryType { get; set; }

        // PG5 & PG8 (Multi-selection)
        public List<string> FitnessGoals { get; set; }
        public List<string> SelectedPrograms { get; set; }

        // Rehab Flag
        public bool IsRehabUser { get; set; }

        // NEW FIELDS
        public string Name { get; set; } = "";
        public string Username { get; set; } = "";
    }
}
