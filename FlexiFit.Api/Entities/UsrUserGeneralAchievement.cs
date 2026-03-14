using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserGeneralAchievement
{
    public int AchievementId { get; set; }

    public int UserId { get; set; }

    public string BadgeKey { get; set; } = null!;

    public DateTime? UnlockedAt { get; set; }

    public virtual UsrUserProfile User { get; set; } = null!;
}
