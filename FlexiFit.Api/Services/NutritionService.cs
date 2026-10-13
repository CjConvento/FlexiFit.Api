using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Mappers;

namespace FlexiFit.Api.Services;

public class NutritionService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<NutritionService> _logger;

    public NutritionService(
        FlexiFitDbContext db,
        ILogger<NutritionService> logger
    )
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Fetches and maps meal groups for a specific daily log.
    /// </summary>
    public async Task<List<MealGroupDto>> GetMealGroupsAsync(int dailyLogId, int templateId, int dayNo, string variationCode, string baseUrl)
    {
        var mealGroups = new List<MealGroupDto>();

        // 1. Get the template day that was used for seeding (matching the calendar)
        var templateDay = await _db.NtrTemplateDays
            .FirstOrDefaultAsync(td => td.TemplateId == templateId && td.DayNo == dayNo && td.VariationCode == variationCode);

        // 2. Fallback to any variation for that day if not found
        if (templateDay == null)
        {
            templateDay = await _db.NtrTemplateDays
                .FirstOrDefaultAsync(td => td.TemplateId == templateId && td.DayNo == dayNo);
        }

        // 3. Ultimate fallback to day 1
        if (templateDay == null)
        {
            templateDay = await _db.NtrTemplateDays
                .FirstOrDefaultAsync(td => td.TemplateId == templateId && td.DayNo == 1);
        }

        if (templateDay == null)
        {
            _logger.LogWarning("No template day found for TemplateId={TemplateId}, DayNo={DayNo}", templateId, dayNo);
            return mealGroups;
        }
        
        // 4. Get all template meals for that day (safe – unique per meal type)
        var templateMeals = await _db.NtrTemplateDayMeals
            .Where(tdm => tdm.TemplateDayId == templateDay.TemplateDayId)
            .ToDictionaryAsync(tdm => tdm.MealType, tdm => tdm.TemplateMealId);

        // 5. Get logged items
        var loggedItems = await _db.NtrDailyMealItemLogs
            .Include(i => i.Food)
            .Where(i => i.DailyLogId == dailyLogId)
            .OrderBy(i => i.SortOrder)
            .ToListAsync();

        var itemsByMeal = loggedItems
            .GroupBy(i => i.MealType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 6. Build DTO for each meal type
        foreach (var mealType in new[] { "B", "L", "S", "D" })
        {
            if (!itemsByMeal.TryGetValue(mealType, out var items))
                continue;

            var foodItems = items.Select(i => new FoodItemDto
            {
                ItemLogId = i.ItemLogId, // added itemlogid here for meal item update
                FoodId = i.FoodId,
                Name = i.Food.FoodName,
                Description = i.Food.Description ?? "",
                ImageUrl = NutritionMapper.BuildFoodImageUrl(baseUrl, i.Food.DietaryType ?? "balanced", mealType, i.Food.ImgFilename),
                DietaryType = i.Food.DietaryType ?? "balanced",
                Qty = (double)i.Qty,
                Unit = i.Food.ServingUnit,
                Calories = (double)i.Calories,
                Protein = (double)i.ProteinG,
                Carbs = (double)i.CarbsG,
                Fats = (double)i.FatsG
            }).ToList();

            mealGroups.Add(new MealGroupDto
            {
                TemplateMealId = templateMeals.GetValueOrDefault(mealType, 0),
                MealType = mealType,
                Status = "PENDING",
                FoodItems = foodItems
            });
        }

        return mealGroups;
    }


}