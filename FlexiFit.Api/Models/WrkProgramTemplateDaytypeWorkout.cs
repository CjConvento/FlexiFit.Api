using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class WrkProgramTemplateDaytypeWorkout
{
    public int DaytypeWId { get; set; }

    public int ProgramId { get; set; }

    public string DayType { get; set; } = null!;

    public int WorkoutId { get; set; }

    public int SetsDefault { get; set; }

    public int RepsDefault { get; set; }

    public int? RestSeconds { get; set; }

    public int WorkoutOrder { get; set; }

    public bool IsPrimaryLift { get; set; }

    public string? MusclePriority { get; set; }

    public virtual WrkProgramTemplate Program { get; set; } = null!;

    public virtual WrkWorkout Workout { get; set; } = null!;
}
