using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class WrkWorkoutLoadStep
{
    public int LoadStepId { get; set; }

    public int WorkoutId { get; set; }

    public string LevelName { get; set; } = null!;

    public int StepNo { get; set; }

    public decimal? LoadKg { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual WrkWorkout Workout { get; set; } = null!;
}
