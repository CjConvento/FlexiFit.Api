using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class VwNtrUserDailySummary
{
    public int UserId { get; set; }

    public DateOnly LogDate { get; set; }

    public int TargetNetCalories { get; set; }

    public string? NutritionGoal { get; set; }

    public int CaloriesConsumed { get; set; }

    public int CaloriesBurned { get; set; }

    public int? NetCalories { get; set; }

    public bool GoalMet { get; set; }

    public DateTime? MarkedDoneAt { get; set; }

    public int WaterMl { get; set; }

    public int ActivityBurnedKcal { get; set; }

    public int ActivityMinutes { get; set; }
}
