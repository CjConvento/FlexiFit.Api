using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrMealPlanCalendar
{
    public int CalendarId { get; set; }

    public int CycleId { get; set; }

    public DateOnly PlanDate { get; set; }

    public int WeekNo { get; set; }

    public int DayNo { get; set; }

    public int TemplateId { get; set; }

    public string VariationCode { get; set; } = null!;

    public bool IsWorkoutDay { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual NtrUserCycleTarget Cycle { get; set; } = null!;

    public virtual NtrMealTemplate Template { get; set; } = null!;
}
