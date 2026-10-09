using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class MealPlanGenerator
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<MealPlanGenerator> _logger;

    public MealPlanGenerator(FlexiFitDbContext db, ILogger<MealPlanGenerator> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Calorie share per meal
    private static readonly Dictionary<string, double> MealPercentages = new()
    {
        { "Breakfast", 0.25 },
        { "Lunch",     0.35 },
        { "Dinner",    0.30 },
        { "Snack",     0.10 }
    };

    private static readonly decimal[] QtyCandidates = 
        { 0.25m, 0.5m, 0.75m, 1.0m, 1.5m, 2.0m, 3.0m, 4.0m };

    private static string GetMealCode(string mealType) => mealType switch
    {
        "Breakfast" => "B",
        "Lunch"     => "L",
        "Dinner"    => "D",
        "Snack"     => "S",
        _           => mealType.Substring(0, 1)
    };

    /// <summary>
    /// Generates macro-balanced meal items for a day. Not persisted here —
    /// caller ang mag-aa-add sa DbContext at mag-save.
    /// </summary>
    public async Task<MealSeedResult> GenerateForDayAsync(
        int userId,
        int dailyLogId,
        MacroTargets targets)
    {
        // Load allergy IDs once
        var userAllergyIds = await _db.NtrUserAllergies
            .Where(ua => ua.UserId == userId)
            .Select(ua => ua.AllergyId)
            .ToListAsync();

        var nutProfile = await _db.NtrUserNutritionProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);
        string dietaryType = NutritionCalculator.NormalizeDiet(nutProfile?.DietaryType);

        var itemLogs = new List<NtrDailyMealItemLog>();
        var mealSummaries = new Dictionary<string, (int cal, decimal prot, decimal carb, decimal fats)>();

        foreach (var (mealType, calPct) in MealPercentages)
        {
            string mealCode = GetMealCode(mealType);

            // Per-meal macro targets
            decimal mealProtTarget = (decimal)(targets.ProteinG * calPct);
            decimal mealCarbTarget = (decimal)(targets.CarbsG   * calPct);
            decimal mealFatTarget  = (decimal)(targets.FatsG    * calPct);

            var items = await SelectFoodsForMeal(
                mealType, dietaryType, userAllergyIds,
                mealProtTarget, mealCarbTarget, mealFatTarget);

            if (items.Count == 0)
            {
                _logger.LogWarning("No safe foods matched for meal {Meal} (diet={Diet}). Skipping.",
                    mealType, dietaryType);
                continue;
            }

            int sortOrder = 1;
            decimal totalCal = 0, totalProt = 0, totalCarb = 0, totalFat = 0;

            foreach (var (food, qty) in items)
            {
                var cal  = food.Calories * qty;
                var prot = food.ProteinG * qty;
                var carb = food.CarbsG   * qty;
                var fat  = food.FatsG    * qty;

                itemLogs.Add(new NtrDailyMealItemLog
                {
                    DailyLogId = dailyLogId,
                    MealType   = mealCode,
                    FoodId     = food.FoodId,
                    Qty        = qty,
                    IsAddon    = false,
                    Calories   = cal,
                    ProteinG   = prot,
                    CarbsG     = carb,
                    FatsG      = fat,
                    SortOrder  = sortOrder++
                });

                totalCal += cal; totalProt += prot; totalCarb += carb; totalFat += fat;
            }

            mealSummaries[mealCode] = ((int)totalCal, totalProt, totalCarb, totalFat);
        }

        return new MealSeedResult
        {
            ItemLogs      = itemLogs,
            MealSummaries = mealSummaries
        };
    }

    /// <summary>
    /// Greedy closest-match: para sa bawat slot, i-evaluate LAHAT ng safe foods
    /// sa LAHAT ng candidate qty, piliin ang (food, qty) na pinaka-malapit sa targets.
    /// </summary>
    private async Task<List<(NtrFoodItem food, decimal qty)>> SelectFoodsForMeal(
        string mealType,
        string dietaryType,
        List<int> userAllergyIds,
        decimal targetProt, decimal targetCarb, decimal targetFat)
    {
        var safeFoods = await _db.NtrFoodItems
            .Where(f => EF.Functions.ILike(f.MealType, mealType)
                    && EF.Functions.ILike(f.DietaryType, dietaryType)
                    && f.IsActive
                    && !f.FoodAllergies.Any(fa => userAllergyIds.Contains(fa.AllergyId)))
            .ToListAsync();

        if (safeFoods.Count == 0) return new();

        var selected = new List<(NtrFoodItem food, decimal qty)>();
        var usedIds = new HashSet<int>();

        decimal curProt = 0, curCarb = 0, curFat = 0;
        int slots = mealType == "Snack" ? 2 : 3;
        const int maxIterations = 8;

        for (int iter = 0; iter < maxIterations && slots > 0; iter++)
        {
            // Check kung sobra na sa target (weighted gap < 0.05 = 5%)
            if (CalcWeightedGap(curProt, curCarb, curFat, targetProt, targetCarb, targetFat) < 0.05)
                break;

            NtrFoodItem bestFood = null!;
            decimal bestQty = 1m;
            double bestScore = double.MaxValue;

            foreach (var food in safeFoods)
            {
                if (usedIds.Contains(food.FoodId)) continue;

                foreach (var qty in QtyCandidates)
                {
                    decimal pProt = curProt + food.ProteinG * qty;
                    decimal pCarb = curCarb + food.CarbsG   * qty;
                    decimal pFat  = curFat  + food.FatsG    * qty;

                    double score = CalcWeightedGap(pProt, pCarb, pFat,
                                                    targetProt, targetCarb, targetFat);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestFood  = food;
                        bestQty   = qty;
                    }
                }
            }

            if (bestFood == null) break;

            curProt += bestFood.ProteinG * bestQty;
            curCarb += bestFood.CarbsG   * bestQty;
            curFat  += bestFood.FatsG    * bestQty;

            selected.Add((bestFood, bestQty));
            usedIds.Add(bestFood.FoodId);
            slots--;
        }

        return selected;
    }

    /// <summary>
    /// Weighted normalized gap (lower = closer to targets).
    /// Protein at carbs weight 4.0, fats weight 3.0.
    /// </summary>
    private static double CalcWeightedGap(
        decimal curProt, decimal curCarb, decimal curFat,
        decimal targetProt, decimal targetCarb, decimal targetFat)
    {
        double Gap(decimal cur, decimal target)
        {
            if (target <= 0) return cur > 0 ? 1.0 : 0.0;
            double raw = Math.Abs((double)(cur - target)) / (double)target;
            return Math.Min(1.0, raw); // cap at 100%
        }

        return Gap(curProt, targetProt) * 4.0
             + Gap(curCarb, targetCarb) * 4.0
             + Gap(curFat,  targetFat)  * 3.0;
    }
}

public class MealSeedResult
{
    public List<NtrDailyMealItemLog> ItemLogs { get; set; } = new();
    public Dictionary<string, (int cal, decimal prot, decimal carb, decimal fats)> MealSummaries { get; set; } = new();
}