using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlexiFit.Api.Entities;

public partial class UsrUserWorkoutSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("session_id")]
    public int SessionId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("program_instance_id")]
    public int ProgramInstanceId { get; set; }

    [Column("workout_day")]
    public int WorkoutDay { get; set; }

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("status")]
    public string Status { get; set; } = null!;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    // ✅ ADD THIS - Navigation property to session workouts
    public virtual ICollection<UsrUserSessionWorkout> UsrUserSessionWorkouts { get; set; } = new List<UsrUserSessionWorkout>();

    // Navigation properties
    public virtual UsrUser User { get; set; } = null!;
    public virtual UsrUserProgramInstance ProgramInstance { get; set; } = null!;
}