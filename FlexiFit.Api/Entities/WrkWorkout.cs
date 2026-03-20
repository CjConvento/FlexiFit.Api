using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class WrkWorkout
{
    public int WorkoutId { get; set; }
    public string WorkoutName { get; set; } = null!;
    public string? MuscleGroup { get; set; }
    public string? Equipment { get; set; }
    public string? Environment { get; set; }
    public string? Category { get; set; }
    public string? DifficultyLevel { get; set; }
    public bool IsWeighted { get; set; }
    public string? Notes { get; set; }
    public int? CaloriesBurned { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? VideoUrl { get; set; }
    public string? ImgFilename { get; set; }
    public int? Duration { get; set; }

    // ✅ ADD THIS - Navigation property to UsrUserSessionWorkout
    public virtual ICollection<UsrUserSessionWorkout> UsrUserSessionWorkouts { get; set; } = new List<UsrUserSessionWorkout>();

    // Existing navigation properties
    public virtual ICollection<WrkProgramTemplateDaytypeWorkout> WrkProgramTemplateDaytypeWorkouts { get; set; } = new List<WrkProgramTemplateDaytypeWorkout>();
    public virtual ICollection<WrkWorkoutLoadStep> WrkWorkoutLoadSteps { get; set; } = new List<WrkWorkoutLoadStep>();
}