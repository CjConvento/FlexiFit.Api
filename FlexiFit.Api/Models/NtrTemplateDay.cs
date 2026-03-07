using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class NtrTemplateDay
{
    public int TemplateDayId { get; set; }

    public int TemplateId { get; set; }

    public string VariationCode { get; set; } = null!;

    public int DayNo { get; set; }

    public string? Notes { get; set; }

    public virtual ICollection<NtrTemplateDayMeal> NtrTemplateDayMeals { get; set; } = new List<NtrTemplateDayMeal>();

    public virtual NtrMealTemplate Template { get; set; } = null!;
}
