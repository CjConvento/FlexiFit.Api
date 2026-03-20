namespace FlexiFit.Api.Dtos
{
    // 1. Main response para sa api/nutrition/today
    public class NutritionScreenDto
    {
        // Progress Stats
        public double TargetCalories { get; set; }
        public double ConsumedCalories { get; set; }
        public double BurnedCalories { get; set; }
        public double NetCalories { get; set; }
        public double RemainingCalories { get; set; }

        // Macro Totals
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

        // ✅ ADD THIS - For session tracking
        public int DailyLogId { get; set; }
    }

    public class MealGroupDto
    {
        public int TemplateMealId { get; set; }
        public string MealType { get; set; } = ""; // B, L, S, D
        public string Status { get; set; } = "PENDING";
        public List<FoodItemDto> FoodItems { get; set; } = new();
    }

    public class FoodItemDto
    {
        public int FoodId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string DietaryType { get; set; } = "";
        public double Qty { get; set; }
        public string Unit { get; set; } = "";
        public double Calories { get; set; }
        public double Protein { get; set; }
        public double Carbs { get; set; }
        public double Fats { get; set; }
    }

    // 2. Request DTO para sa pag-complete ng nutrition day
    public class LogFullDayRequest
    {
        public int CycleId { get; set; }
        public List<LogMealEntry> Meals { get; set; } = new();
    }

    public class LogMealEntry
    {
        public string MealType { get; set; } = "B";
        public double TotalCalories { get; set; }
        public double TotalProtein { get; set; }
        public double TotalCarbs { get; set; }
        public double TotalFats { get; set; }
    }

    // 3. Response DTO para sa completion
    public class NutritionCompleteResultDto
    {
        public string Message { get; set; } = "";
        public int CurrentDay { get; set; }
        public bool IsCompleted { get; set; }
    }

    // 4. DTO para sa history detail
    public class NutritionHistoryDetailDto
    {
        public int Day { get; set; }
        public int Week { get; set; }
        public NutritionScreenDto Nutrition { get; set; } = new();
    }

    // Add to NutritionDtos.cs

    // Water tracking DTOs
    public class AddWaterRequest
    {
        public int AmountMl { get; set; }
    }

    public class WaterResponse
    {
        public int WaterMl { get; set; }
    }

    // Food details DTOs
    public class FoodDetailsResponse
    {
        public int FoodId { get; set; }
        public string FoodName { get; set; } = "";
        public string? Description { get; set; }
        public double Calories { get; set; }
        public double ProteinG { get; set; }
        public double CarbsG { get; set; }
        public double FatsG { get; set; }
        public string ServingUnit { get; set; } = "";
        public string? ImgFilename { get; set; }
    }

    // Meal item update DTOs
    public class UpdateMealItemRequest
    {
        public decimal NewQty { get; set; }
    }

    public class SwapFoodRequest
    {
        public int NewFoodId { get; set; }
    }
}