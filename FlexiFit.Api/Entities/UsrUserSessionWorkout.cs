using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FlexiFit.Api.Entities;

public partial class UsrUserSessionWorkout
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("session_workout_id")]
    public int SessionWorkoutId { get; set; }

    [Column("session_id")]
    public int SessionId { get; set; }

    [Column("workout_id")]
    public int WorkoutId { get; set; }

    [Column("sets")]
    public int Sets { get; set; }

    [Column("reps")]
    public int Reps { get; set; }

    [Column("load_kg")]
    public decimal? LoadKg { get; set; }

    [Column("order_no")]
    public int OrderNo { get; set; }

    // ✅ ONLY these navigation properties - NO UsrUserSessionInstance!
    [ForeignKey("SessionId")]
    public virtual UsrUserWorkoutSession Session { get; set; } = null!;

    [ForeignKey("WorkoutId")]
    public virtual WrkWorkout Workout { get; set; } = null!;

    // ❌ REMOVE THIS IF IT EXISTS:
    // public virtual UsrUserSessionInstance SessionInstance { get; set; }
    // public int? SessionInstanceId { get; set; }
}