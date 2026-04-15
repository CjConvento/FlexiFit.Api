using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class NtrFoodItem
{
    public int FoodId { get; set; }

    public string FoodName { get; set; } = null!;

    public string MealType { get; set; } = null!;

    public string DietaryType { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string SizeType { get; set; } = null!;

    public string ServingUnit { get; set; } = null!;

    public decimal ServingWeightG { get; set; }

    public decimal Calories { get; set; }

    public decimal ProteinG { get; set; }

    public decimal CarbsG { get; set; }

    public decimal FatsG { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? ImgFilename { get; set; }

    public string? Description { get; set; }

    public virtual ICollection<NtrDailyMealItemLog> NtrDailyMealItemLogs { get; set; } = new List<NtrDailyMealItemLog>();

    public virtual ICollection<NtrTemplateMealItem> NtrTemplateMealItems { get; set; } = new List<NtrTemplateMealItem>();

    public virtual ICollection<NtrFoodAllergies> FoodAllergies { get; set; } = new List<NtrFoodAllergies>();
}
