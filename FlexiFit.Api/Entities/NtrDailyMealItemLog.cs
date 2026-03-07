using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrDailyMealItemLog
{
    public int ItemLogId { get; set; }

    public int DailyLogId { get; set; }

    public string MealType { get; set; } = null!;

    public int FoodId { get; set; }

    public decimal Qty { get; set; }

    public bool IsAddon { get; set; }

    public decimal Calories { get; set; }

    public decimal ProteinG { get; set; }

    public decimal CarbsG { get; set; }

    public decimal FatsG { get; set; }

    public int SortOrder { get; set; }

    public virtual NtrDailyLog DailyLog { get; set; } = null!;

    public virtual NtrFoodItem Food { get; set; } = null!;
}
