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


public class LogMeal // <--- Dapat ganito ang pangalan dito
{
    public int CycleId { get; set; }
    public string MealType { get; set; } = "B";
    public double TotalCalories { get; set; }
    public double TotalProtein { get; set; }
    public double TotalCarbs { get; set; }
    public double TotalFats { get; set; }
}

// 2. 🔥 ANG SOLUSYON SA "SABAY-SABAY": Bulk Log Request
public class LogFullDayRequest
{
    public int CycleId { get; set; }
    // Listahan ito para isang request lang, kasama na B, L, S, D!
    public List<LogMealEntry> Meals { get; set; } = new();
}

public class LogMealEntry
{
    public string MealType { get; set; } = "B"; // B, L, S, D
    public double TotalCalories { get; set; }
    public double TotalProtein { get; set; }
    public double TotalCarbs { get; set; }
    public double TotalFats { get; set; }
}