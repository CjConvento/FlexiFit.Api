using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class UsrUserProfileVersion
{
    public int ProfileVersionId { get; set; }

    public int UserId { get; set; }

    public string FitnessLevelSelected { get; set; } = null!;

    public string GoalSelected { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public bool IsCurrent { get; set; }

    public virtual UsrUser User { get; set; } = null!;

    public virtual ICollection<UsrUserProgramAchievement> UsrUserProgramAchievements { get; set; } = new List<UsrUserProgramAchievement>();

    public virtual ICollection<UsrUserProgramInstance> UsrUserProgramInstances { get; set; } = new List<UsrUserProgramInstance>();

    public virtual ICollection<UsrUserWorkoutProgress> UsrUserWorkoutProgresses { get; set; } = new List<UsrUserWorkoutProgress>();
}
