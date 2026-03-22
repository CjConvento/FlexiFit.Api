using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlexiFit.Api.Entities
{
    [Table("wkt_workout_calendars")]
    public class WktWorkoutCalendar
    {
        [Key]
        [Column("calendar_id")]
        public int CalendarId { get; set; }

        [Column("user_id")]
        public int UserId { get; set; }

        [Column("cycle_id")]
        public int CycleId { get; set; }

        [Column("plan_date")]
        public DateOnly PlanDate { get; set; }

        [Column("week_no")]
        public int WeekNo { get; set; }

        [Column("day_no")]
        public int DayNo { get; set; }

        [Column("template_id")]
        public int TemplateId { get; set; }

        [Column("variation_code")]
        public string VariationCode { get; set; } = "A";

        [Column("is_workout_day")]
        public bool IsWorkoutDay { get; set; }

        [Column("status")]
        public string Status { get; set; } = "PENDING";

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }

        // Navigation property (optional)
        [ForeignKey(nameof(UserId))]
        public virtual UsrUser User { get; set; }
    }
}