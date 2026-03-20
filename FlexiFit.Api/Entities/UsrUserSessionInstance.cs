using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations; // Kailangan ito para sa [Key]
using System.ComponentModel.DataAnnotations.Schema; // Kailangan ito para sa [DatabaseGenerated]

namespace FlexiFit.Api.Entities;

public partial class UsrUserSessionInstance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)] // ITO ANG PINAKA-IMPORTANTE!
    [Column("session_id")]
    public int SessionId { get; set; }

    [Column("instance_id")]
    public int InstanceId { get; set; }

    [Column("month_no")]
    public int MonthNo { get; set; }

    [Column("week_no")]
    public int WeekNo { get; set; }

    [Column("day_no")]
    public int DayNo { get; set; }

    [Column("day_type")]
    public string DayType { get; set; } = null!;

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    public virtual UsrUserProgramInstance Instance { get; set; } = null!;

    public virtual ICollection<UsrUserSessionWorkout> UsrUserSessionWorkouts { get; set; } = new List<UsrUserSessionWorkout>();
}