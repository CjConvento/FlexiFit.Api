namespace FlexiFit.Api.Mappers;

public static class NutritionMapper
{
    public static string BuildFoodImageUrl(string? baseUrl, string? dietaryType, string mealType, string? fileName)
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