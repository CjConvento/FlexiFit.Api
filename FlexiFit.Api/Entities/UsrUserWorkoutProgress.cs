using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserWorkoutProgress
{
    public int UserId { get; set; }

    public int ProfileVersionId { get; set; }

    public int WorkoutId { get; set; }

    public string CurrentLevel { get; set; } = null!;

    public int CurrentStepNo { get; set; }

    public bool IsMastered { get; set; }

    public DateTime? MasteredAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UsrUserProfileVersion ProfileVersion { get; set; } = null!;

    public virtual UsrUser User { get; set; } = null!;
}
