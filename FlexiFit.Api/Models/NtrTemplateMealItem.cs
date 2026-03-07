using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class NtrTemplateMealItem
{
    public int TemplateItemId { get; set; }

    public int TemplateMealId { get; set; }

    public int FoodId { get; set; }

    public decimal DefaultQty { get; set; }

    public bool IsOptionalAddon { get; set; }

    public int SortOrder { get; set; }

    public virtual NtrFoodItem Food { get; set; } = null!;

    public virtual NtrTemplateDayMeal TemplateMeal { get; set; } = null!;
}
