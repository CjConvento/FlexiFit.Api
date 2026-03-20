    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;

    namespace FlexiFit.Api.Entities // Siguraduhin na match ito sa namespace ng ibang files sa folder na yan
    {
        [Table("usr_user_onboarding_details")]
        public class UsrUserOnboardingDetail
        {
            [Key]
            [Column("id")]
            public int Id { get; set; }

            [Column("user_id")]
            public int UserId { get; set; }

            [Column("upper_body_injury")]
            public bool UpperBodyInjury { get; set; }

            [Column("lower_body_injury")]
            public bool LowerBodyInjury { get; set; }

            [Column("joint_problems")]
            public bool JointProblems { get; set; }

            [Column("short_breath")]
            public bool ShortBreath { get; set; }

            [Column("health_none")]
            public bool HealthNone { get; set; }

            [Column("activity_level")]
            public string? ActivityLevel { get; set; }

            [Column("fitness_level")]
            public string? FitnessLevel { get; set; }

            [Column("environment")]
            public string? Environment { get; set; }

            [Column("fitness_goals")]
            public string? FitnessGoals { get; set; }

            [Column("body_goal")]
            public string? BodyGoal { get; set; }

            [Column("diet_type")]
            public string? DietType { get; set; }

            [Column("selected_programs")] 
            public string? SelectedPrograms { get; set; }

            [Column("created_at")]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            // 🔥 ETO YUNG UPDATE BABE:
            [Column("updated_at")]
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }
    }