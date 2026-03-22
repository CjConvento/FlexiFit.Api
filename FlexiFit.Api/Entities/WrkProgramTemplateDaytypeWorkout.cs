using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class WrkProgramTemplateDaytypeWorkout
{
    public int DaytypeWId { get; set; }

    public int ProgramId { get; set; }

    // Add the week number property
    public int WeekNo { get; set; } = 1; // default to 1 as per database DEFAULT ((1))

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