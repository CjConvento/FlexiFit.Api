using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class NtrMealTemplate
{
    public int TemplateId { get; set; }

    public string TemplateName { get; set; } = null!;

    public string? DietaryType { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<NtrMealPlanCalendar> NtrMealPlanCalendars { get; set; } = new List<NtrMealPlanCalendar>();

    public virtual ICollection<NtrTemplateDay> NtrTemplateDays { get; set; } = new List<NtrTemplateDay>();
}
