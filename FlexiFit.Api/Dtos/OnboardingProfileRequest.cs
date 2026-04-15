using System.ComponentModel.DataAnnotations;

namespace FlexiFit.Api.Dtos
{
    using System.Text.Json.Serialization; // Import mo 'to babe

    public class OnboardingProfileRequest
    {
        // --- DINAGDAG NATIN 'TO PARA SA USR_USER_PROFILES ---
        [JsonPropertyName("Name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("Username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("Age")] // Match sa @SerializedName("Age")
        public int Age { get; set; }

        [JsonPropertyName("Gender")]
        public string Gender { get; set; } = "";

        [JsonPropertyName("HeightCm")]
        public decimal HeightCm { get; set; }

        [JsonPropertyName("WeightKg")]
        public decimal WeightKg { get; set; }

        [JsonPropertyName("TargetWeightKg")]
        public decimal TargetWeightKg { get; set; }

        [JsonPropertyName("UpperBodyInjury")]
        public bool UpperBodyInjury { get; set; }

        [JsonPropertyName("LowerBodyInjury")]
        public bool LowerBodyInjury { get; set; }

        [JsonPropertyName("JointProblems")]
        public bool JointProblems { get; set; }

        [JsonPropertyName("ShortBreath")]
        public bool ShortBreath { get; set; }

        [JsonPropertyName("HealthNone")]
        public bool HealthNone { get; set; }

        [JsonPropertyName("FitnessLifestyle")] // Match sa Kotlin
        public string ActivityLevel { get; set; } = "";

        [JsonPropertyName("FitnessLevel")]
        public string FitnessLevel { get; set; } = "";

        [JsonPropertyName("Environment")]
        public List<string> Environment { get; set; } = new();

        [JsonPropertyName("FitnessGoals")] // Match sa Kotlin
        public List<string> FitnessGoals { get; set; } = new();

        [JsonPropertyName("BodyGoal")]
        public string BodyGoal { get; set; } = "";

        [JsonPropertyName("DietType")]
        public string DietType { get; set; } = "";

        [JsonPropertyName("SelectedPrograms")]
        public List<DetailedProgramDto> SelectedPrograms { get; set; } = new();

        [JsonPropertyName("IsRehab")]
        public bool IsRehab { get; set; }

        [JsonPropertyName("Allergies")]
        public List<string> Allergies { get; set; } = new();
    }

    // Gawa ka rin ng maliit na class na 'to sa parehong file para sa programs
    public class DetailedProgramDto
    {
        [JsonPropertyName("RawName")] // Match sa RawName sa Kotlin
        public string Name { get; set; } = "";

        [JsonPropertyName("Category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("Level")]
        public string Level { get; set; } = "";

        [JsonPropertyName("Environment")]
        public string Environment { get; set; } = "";
    }
}