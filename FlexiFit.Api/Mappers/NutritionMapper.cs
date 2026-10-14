using FlexiFit.Api.Dtos;

namespace FlexiFit.Api.Mappers;

public static class NutritionMapper
{
    // ─ Full NutritionScreenDto Builder ──────────────────────────────
    public static NutritionScreenDto BuildScreenDto(
        double baseTarget, double bonusCalories, double totalTarget,
        double consumedCal, double burnedCal,
        double targetProtein, double consumedProtein,
        double targetCarbs, double consumedCarbs,
        double targetFats, double consumedFats,
        int waterConsumedMl, int waterTargetMl,
        List<MealGroupDto> meals, int dailyLogId,
        string templateName, int DayNo, int weekNo)
    {
        double netCalories = consumedCal - burnedCal;

        return new NutritionScreenDto
        {
            BaseTarget = baseTarget,
            BonusCalories = bonusCalories,
            TotalTarget = totalTarget,
            TargetCalories = totalTarget,
            ConsumedCalories = consumedCal,
            BurnedCalories = burnedCal,
            NetCalories = netCalories,
            RemainingCalories = Math.Max(0, totalTarget - consumedCal),
            TargetProtein = targetProtein,
            ConsumedProtein = consumedProtein,
            TargetCarbs = targetCarbs,
            ConsumedCarbs = consumedCarbs,
            TargetFats = targetFats,
            ConsumedFats = consumedFats,

            WaterConsumedMl = waterConsumedMl,
            WaterTargetMl = waterTargetMl,
            Meals = meals,
            DailyLogId = dailyLogId,
            TemplateName = templateName, 

            // Day Properties
            DayNo = DayNo,
            WeekNo = weekNo // Calculate week number based on day number
        };
    }

    // ── Food Image URL ───────────────────────────────────────────────
    public static string BuildFoodImageUrl(string? baseUrl, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return $"{baseUrl}/images/foods/default.png";
        if (fileName.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return fileName;
        return $"{baseUrl}/images/foods/default.png"; // Fallback for legacy
    }

    public static int GetMealOrder(string mealType) => mealType switch
    {
        "B" => 1, "L" => 2, "S" => 3, "D" => 4, _ => 5 
    };
}