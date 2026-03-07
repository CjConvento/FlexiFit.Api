using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class DailyProgressLog
{
    public int ProgressId { get; set; }

    public int UserId { get; set; }

    public int InstanceId { get; set; }

    public int MonthNo { get; set; }

    public int WeekNo { get; set; }

    public int DayNo { get; set; }

    public string? FitnessLevelSnapshot { get; set; }

    public int? CaloriesBurned { get; set; }

    public int? CaloriesIntake { get; set; }

    public int? WaterMl { get; set; }

    public bool MealPlanCompleted { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UsrUserProgramInstance Instance { get; set; } = null!;

    public virtual UsrUser User { get; set; } = null!;
}
