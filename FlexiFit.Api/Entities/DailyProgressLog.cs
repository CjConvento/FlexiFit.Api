using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlexiFit.Api.Entities
{
    [Table("daily_progress_log")]
    public class DailyProgressLog
    {
        [Key]
        public int ProgressId { get; set; }

        public int UserId { get; set; }
        public int InstanceId { get; set; }
        public int MonthNo { get; set; }
        public int WeekNo { get; set; }
        public int DayNo { get; set; }
        public string? FitnessLevelSnapshot { get; set; }
        public int? CaloriesBurned { get; set; }
        public int? CaloriesIntake { get; set; }
        public int? WaterMl { get; set; }
        public bool MealPlanCompleted { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey(nameof(UserId))]
        public virtual UsrUser? User { get; set; }

        [ForeignKey(nameof(InstanceId))]
        public virtual UsrUserProgramInstance? ProgramInstance { get; set; }
    }
}