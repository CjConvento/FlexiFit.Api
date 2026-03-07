using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUser
{
    public int UserId { get; set; }

    public string FirebaseUid { get; set; } = null!;

    public string? Email { get; set; }

    public string? Name { get; set; }

    public string? Username { get; set; }

    public string? Address { get; set; }

    public bool IsVerified { get; set; }

    public string Role { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<ActActivitySummary> ActActivitySummaries { get; set; } = new List<ActActivitySummary>();

    public virtual ICollection<DailyProgressLog> DailyProgressLogs { get; set; } = new List<DailyProgressLog>();

    public virtual ICollection<NtrDailyLog> NtrDailyLogs { get; set; } = new List<NtrDailyLog>();

    public virtual ICollection<NtrUserCycleTarget> NtrUserCycleTargets { get; set; } = new List<NtrUserCycleTarget>();

    public virtual NtrUserNutritionProfile? NtrUserNutritionProfile { get; set; }

    public virtual ICollection<NtrWaterLog> NtrWaterLogs { get; set; } = new List<NtrWaterLog>();

    public virtual ICollection<UsrDeviceToken> UsrDeviceTokens { get; set; } = new List<UsrDeviceToken>();

    public virtual ICollection<UsrUserMetric> UsrUserMetrics { get; set; } = new List<UsrUserMetric>();

    public virtual UsrUserProfile? UsrUserProfile { get; set; }

    public virtual UsrUserProfileVersion? UsrUserProfileVersion { get; set; }

    public virtual ICollection<UsrUserProgramAchievement> UsrUserProgramAchievements { get; set; } = new List<UsrUserProgramAchievement>();

    public virtual ICollection<UsrUserProgramInstance> UsrUserProgramInstances { get; set; } = new List<UsrUserProgramInstance>();

    public virtual ICollection<UsrUserWorkoutProgress> UsrUserWorkoutProgresses { get; set; } = new List<UsrUserWorkoutProgress>();
}
