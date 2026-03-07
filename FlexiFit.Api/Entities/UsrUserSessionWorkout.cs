using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserSessionWorkout
{
    public int SessionWorkoutId { get; set; }

    public int SessionId { get; set; }

    public int WorkoutId { get; set; }

    public int Sets { get; set; }

    public int Reps { get; set; }

    public decimal? LoadKg { get; set; }

    public int OrderNo { get; set; }

    public virtual UsrUserSessionInstance Session { get; set; } = null!;
}
