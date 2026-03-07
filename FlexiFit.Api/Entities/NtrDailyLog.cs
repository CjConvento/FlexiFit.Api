using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrDailyLog
{
    public int DailyLogId { get; set; }

    public int UserId { get; set; }

    public int CycleId { get; set; }

    public DateOnly PlanDate { get; set; }

    public int TargetNetCalories { get; set; }

    public string GoalType { get; set; } = null!;

    public int CaloriesConsumed { get; set; }

    public int CaloriesBurned { get; set; }

    public int? NetCalories { get; set; }

    public bool GoalMet { get; set; }

    public DateTime? MarkedDoneAt { get; set; }

    public virtual NtrUserCycleTarget Cycle { get; set; } = null!;

    public virtual ICollection<NtrDailyMealItemLog> NtrDailyMealItemLogs { get; set; } = new List<NtrDailyMealItemLog>();

    public virtual ICollection<NtrDailyMealLog> NtrDailyMealLogs { get; set; } = new List<NtrDailyMealLog>();

    public virtual UsrUser User { get; set; } = null!;
}
