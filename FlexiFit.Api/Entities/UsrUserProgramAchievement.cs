using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserProgramAchievement
{
    public int AchievementId { get; set; }

    public int UserId { get; set; }

    public int ProgramId { get; set; }

    public int ProfileVersionId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CompletedAt { get; set; }

    public int CompletedCount { get; set; }

    public virtual UsrUserProfileVersion ProfileVersion { get; set; } = null!;

    public virtual UsrUser User { get; set; } = null!;
}
