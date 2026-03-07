using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrUserCycleTarget
{
    public int CycleId { get; set; }

    public int UserId { get; set; }

    public DateOnly StartDate { get; set; }

    public int WeeksInCycle { get; set; }

    public int DailyTargetNetCalories { get; set; }

    public string GoalType { get; set; } = null!;

    public decimal? ProteinTargetG { get; set; }

    public decimal? CarbsTargetG { get; set; }

    public decimal? FatsTargetG { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<NtrDailyLog> NtrDailyLogs { get; set; } = new List<NtrDailyLog>();

    public virtual ICollection<NtrMealPlanCalendar> NtrMealPlanCalendars { get; set; } = new List<NtrMealPlanCalendar>();

    public virtual UsrUser User { get; set; } = null!;
}
