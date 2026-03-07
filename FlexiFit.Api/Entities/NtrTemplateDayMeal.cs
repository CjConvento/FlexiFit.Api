using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrTemplateDayMeal
{
    public int TemplateMealId { get; set; }

    public int TemplateDayId { get; set; }

    public string MealType { get; set; } = null!;

    public decimal? TargetSharePct { get; set; }

    public virtual ICollection<NtrTemplateMealItem> NtrTemplateMealItems { get; set; } = new List<NtrTemplateMealItem>();

    public virtual NtrTemplateDay TemplateDay { get; set; } = null!;
}
