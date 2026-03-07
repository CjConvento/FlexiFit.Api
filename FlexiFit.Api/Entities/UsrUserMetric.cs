using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserMetric
{
    public int MetricId { get; set; }

    public int UserId { get; set; }

    public decimal? CurrentWeightKg { get; set; }

    public decimal? CurrentHeightCm { get; set; }

    public string FitnessGoal { get; set; } = null!;

    public string NutritionGoal { get; set; } = null!;

    public int? CalorieTarget { get; set; }

    public int? ProteinTargetG { get; set; }

    public int? CarbsTargetG { get; set; }

    public int? FatsTargetG { get; set; }

    public DateTime RecordedAt { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
