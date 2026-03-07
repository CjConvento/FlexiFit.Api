using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class UsrUserSessionInstance
{
    public int SessionId { get; set; }

    public int InstanceId { get; set; }

    public int MonthNo { get; set; }

    public int WeekNo { get; set; }

    public int DayNo { get; set; }

    public string DayType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual UsrUserProgramInstance Instance { get; set; } = null!;

    public virtual ICollection<UsrUserSessionWorkout> UsrUserSessionWorkouts { get; set; } = new List<UsrUserSessionWorkout>();
}
