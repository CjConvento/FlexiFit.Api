using System.ComponentModel.DataAnnotations;

namespace FlexiFit.Api.Dtos
{
    public class OnboardingProfileRequest
    {
        [Required]
        [Range(7, 100)]
        public int Age { get; set; }

        [Required]
        [RegularExpression("^(Male|Female)$", ErrorMessage = "Gender must be 'Male' or 'Female'")]
        public string Gender { get; set; } = "";

        [Required]
        [Range(100, 250)]
        public decimal HeightCm { get; set; } // Changed to decimal for BMI precision

        [Required]
        [Range(30, 300)]
        public decimal WeightKg { get; set; } // Changed to decimal

        [Required]
        [Range(30, 300)]
        public decimal TargetWeightKg { get; set; } // Changed to decimal

        // Health concerns (Guds na 'to)
        public bool UpperBodyInjury { get; set; }
        public bool LowerBodyInjury { get; set; }
        public bool JointProblems { get; set; }
        public bool ShortBreath { get; set; }
        public bool HealthNone { get; set; }

        [Required]
        public string ActivityLevel { get; set; } = ""; // e.g., "Sedentary", "Active"

        [Required]
        public string FitnessLevel { get; set; } = ""; // e.g., "Beginner"

        [Required]
        [MinLength(1, ErrorMessage = "Please select at least one environment.")]
        public List<string> Environment { get; set; } = new();

        [Required]
        [MinLength(1, ErrorMessage = "Please select at least one fitness goal.")]
        public List<string> FitnessGoals { get; set; } = new();

        [Required]
        public string BodyGoal { get; set; } = ""; // e.g., "lose_weight", "build_muscle"

        [Required]
        public string DietType { get; set; } = "";

        // Para sa gym program selection
        public List<string> SelectedPrograms { get; set; } = new();
    }
}