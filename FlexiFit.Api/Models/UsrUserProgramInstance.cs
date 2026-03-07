using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class UsrUserProgramInstance
{
    public int InstanceId { get; set; }

    public int UserId { get; set; }

    public int ProgramId { get; set; }

    public int ProfileVersionId { get; set; }

    public int CycleNo { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CompletedAt { get; set; }

    public string? FitnessLevelAtStart { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<DailyProgressLog> DailyProgressLogs { get; set; } = new List<DailyProgressLog>();

    public virtual UsrUserProfileVersion ProfileVersion { get; set; } = null!;

    public virtual UsrUser User { get; set; } = null!;

    public virtual ICollection<UsrUserSessionInstance> UsrUserSessionInstances { get; set; } = new List<UsrUserSessionInstance>();
}
