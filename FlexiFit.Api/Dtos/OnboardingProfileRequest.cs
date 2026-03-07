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
        public string ActivityLevel { get; set; } = "";

        [Required]
        public string BodyGoal { get; set; } = "";

        [Required]
        public string DietType { get; set; } = "";
    }
}