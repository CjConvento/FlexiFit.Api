using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrUserNutritionProfile
{
    public int UserId { get; set; }

    public int? Age { get; set; }

    public decimal? WeightKg { get; set; }

    public decimal? HeightCm { get; set; }

    public string NutritionGoal { get; set; } = null!;

    public string ActivityLevel { get; set; } = null!;

    public string? DietaryType { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsProfileComplete { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
