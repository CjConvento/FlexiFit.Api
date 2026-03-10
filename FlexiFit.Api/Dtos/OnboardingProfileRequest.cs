using System.ComponentModel.DataAnnotations;

namespace FlexiFit.Api.Dtos
{
    public class OnboardingProfileRequest
    {
        [Required]
        [Range(7, 100)]
        public int Age { get; set; }

        [Required]
        public string Gender { get; set; } = "";

        [Required]
        [Range(100, 250)]
        public double HeightCm { get; set; }

        [Required]
        [Range(30, 300)]
        public double WeightKg { get; set; }

        [Required]
        [Range(30, 300)]
        public double TargetWeightKg { get; set; }

        public bool UpperBodyInjury { get; set; }
        public bool LowerBodyInjury { get; set; }
        public bool JointProblems { get; set; }
        public bool ShortBreath { get; set; }
        public bool HealthNone { get; set; }

        [Required]
        public string ActivityLevel { get; set; } = "";

        [Required]
        public string FitnessLevel { get; set; } = "";

        [Required]
        [MinLength(1)]
        public List<string> Environment { get; set; } = new();

        [Required]
        [MinLength(1)]
        public List<string> FitnessGoals { get; set; } = new();

        [Required]
        public string BodyGoal { get; set; } = "";

        [Required]
        public string DietType { get; set; } = "";

        public List<string> SelectedPrograms { get; set; } = new();
    }
}


