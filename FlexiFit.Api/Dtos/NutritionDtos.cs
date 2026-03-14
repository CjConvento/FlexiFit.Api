namespace FlexiFit.Api.Dtos;

public class NutritionScreenDto
{
    // Progress Stats (Engine-Computed)
    public double TargetCalories { get; set; }
    public double ConsumedCalories { get; set; }
    public double BurnedCalories { get; set; }   // BAGONG DAGDAG: Mula sa Activity Engine
    public double NetCalories { get; set; }      // BAGONG DAGDAG: Consumed - Burned
    public double RemainingCalories { get; set; } // BAGONG DAGDAG: Target - Net

    // Macro Totals (Para sa mga progress bars sa Nutrition Root)
    public double TargetProtein { get; set; }
    public double ConsumedProtein { get; set; }
    public double TargetCarbs { get; set; }
    public double ConsumedCarbs { get; set; }
    public double TargetFats { get; set; }
    public double ConsumedFats { get; set; }

    // Water Info
    public int WaterConsumedMl { get; set; }
    public int WaterTargetMl { get; set; }

    // Today's Meals
    public List<MealGroupDto> Meals { get; set; } = new();
}

public class MealGroupDto
{
    public int TemplateMealId { get; set; } // Reference for logging (from ntr_template_day_meals)
    public string MealType { get; set; } = ""; // B, L, S, D
    public string Status { get; set; } = "PENDING"; // PENDING, DONE, SKIPPED
    public List<FoodItemDto> FoodItems { get; set; } = new();
}

public class FoodItemDto
{
    public int FoodId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = ""; // Mula sa description column mo
    public string ImageUrl { get; set; } = "";    // Mula sa img_filename

    public string DietaryType { get; set; } = ""; // Mula sa DietCategory, ginawa nating DietaryType

    // Quantity Info
    public double Qty { get; set; }
    public string Unit { get; set; } = "";        // e.g., "Bowl", "Cup", "Piece"

    // Macros per Item (Para kapag clinick ni user, makikita yung details)
    public double Calories { get; set; }
    public double Protein { get; set; }
    public double Carbs { get; set; }
    public double Fats { get; set; }
}