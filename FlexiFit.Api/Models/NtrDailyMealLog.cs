using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class NtrDailyMealLog
{
    public int MealLogId { get; set; }

    public int DailyLogId { get; set; }

    public string MealType { get; set; } = null!;

    public int Calories { get; set; }

    public decimal ProteinG { get; set; }

    public decimal CarbsG { get; set; }

    public decimal FatsG { get; set; }

    public virtual NtrDailyLog DailyLog { get; set; } = null!;
}
