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
        // 1. Load REGULAR foods for this meal (meal_type = Breakfast/Lunch/Dinner/Snack)
        var regularFoods = await _db.NtrFoodItems
            .Where(f => EF.Functions.ILike(f.MealType, mealType)
                    && EF.Functions.ILike(f.DietaryType, dietaryType)
                    && f.IsActive
                    && !f.FoodAllergies.Any(fa => userAllergyIds.Contains(fa.AllergyId)))
            .ToListAsync();

        if (regularFoods.Count == 0) return new();

        // 2. Load CARB_ADDON pool (meal_type = 'Add-on')
        var carbAddons = await _db.NtrFoodItems
            .Where(f => EF.Functions.ILike(f.MealType, "Add-on")
                    && EF.Functions.ILike(f.DietaryType, "CARB_ADDON")
                    && f.IsActive
                    && !f.FoodAllergies.Any(fa => userAllergyIds.Contains(fa.AllergyId)))
            .ToListAsync();

        // 3. Load FRUIT_ADDON pool (meal_type = 'Add-on')
        var fruitAddons = await _db.NtrFoodItems
            .Where(f => EF.Functions.ILike(f.MealType, "Add-on")
                    && EF.Functions.ILike(f.DietaryType, "FRUIT_ADDON")
                    && f.IsActive
                    && !f.FoodAllergies.Any(fa => userAllergyIds.Contains(fa.AllergyId)))
            .ToListAsync();

        var selected = new List<(NtrFoodItem food, decimal qty)>();
        var usedIds = new HashSet<int>();

        decimal curProt = 0, curCarb = 0, curFat = 0;

        // ═══════════════════════════════════════════════════════════
        //  STEP 1: Force CARB_ADDON (rice) slot for BALANCED Lunch/Dinner
        // ═══════════════════════════════════════════════════════════
        bool isBalanced = dietaryType.Equals("BALANCED", StringComparison.OrdinalIgnoreCase);
        bool needsRice = isBalanced && (mealType == "Lunch" || mealType == "Dinner");

        if (needsRice && carbAddons.Any())
        {
            // Prefer white rice, else highest carb/cal ratio
            var bestRice = carbAddons
                .OrderByDescending(f => f.FoodName.Contains("Rice", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(f => f.CarbsG / Math.Max(f.Calories, 1m))
                .First();

            // Fill ~50% of target carbs from rice
            decimal riceQty = 1.0m;
            if (bestRice.CarbsG > 0 && targetCarb > 0)
            {
                riceQty = (targetCarb * 0.5m) / bestRice.CarbsG;
                riceQty = Math.Clamp(riceQty, 0.5m, 2.0m);
            }

            selected.Add((bestRice, riceQty));
            usedIds.Add(bestRice.FoodId);
            curProt += bestRice.ProteinG * riceQty;
            curCarb += bestRice.CarbsG   * riceQty;
            curFat  += bestRice.FatsG    * riceQty;

            _logger.LogDebug("Rice slot for {Meal}: {Food} x{Qty}",
                mealType, bestRice.FoodName, riceQty);
        }

        // ═══════════════════════════════════════════════════════════
        //  STEP 2: Greedy loop for regular foods (with overshoot prevention)
        // ═══════════════════════════════════════════════════════════
        int maxSlots = mealType == "Snack" ? 2 : 3;
        int minSlots = mealType == "Snack" ? 1 : 2;
        const int maxIterations = 8;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            int itemsPicked = selected.Count;
            if (itemsPicked >= maxSlots) break;

            double currentScore = CalcWeightedGap(curProt, curCarb, curFat,
                                                   targetProt, targetCarb, targetFat);

            // Early exit if good enough AND minimum items picked
            if (currentScore < 0.05) break;
            if (currentScore < 0.08 && itemsPicked >= minSlots) break;

            NtrFoodItem bestFood = null!;
            decimal bestQty = 1m;
            double bestScore = double.MaxValue;

            foreach (var food in regularFoods)
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

            //  OVERSHOOT PREVENTION: Don't add if it worsens score
            if (bestScore >= currentScore)
            {
                _logger.LogDebug("{Meal}: stop — best candidate worsens score ({Best:F2} >= {Cur:F2})",
                    mealType, bestScore, currentScore);
                break;
            }

            curProt += bestFood.ProteinG * bestQty;
            curCarb += bestFood.CarbsG   * bestQty;
            curFat  += bestFood.FatsG    * bestQty;

            selected.Add((bestFood, bestQty));
            usedIds.Add(bestFood.FoodId);
        }

        // ═══════════════════════════════════════════════════════════
        //  STEP 3: Optional FRUIT_ADDON top-up (if carbs still short)
        // ═══════════════════════════════════════════════════════════
        if (fruitAddons.Any() && selected.Count < maxSlots)
        {
            double gapAfter = CalcWeightedGap(curProt, curCarb, curFat,
                                               targetProt, targetCarb, targetFat);

            // Add fruit only if we're meaningfully short on carbs (not keto)
            bool isKeto = dietaryType.Equals("KETO", StringComparison.OrdinalIgnoreCase);
            if (!isKeto && gapAfter > 0.10 && curCarb < targetCarb * 0.9m)
            {
                var bestFruit = fruitAddons
                    .Where(f => !usedIds.Contains(f.FoodId))
                    .OrderByDescending(f => f.CarbsG / Math.Max(f.Calories, 1m))
                    .FirstOrDefault();

                if (bestFruit != null)
                {
                    decimal remaining = targetCarb - curCarb;
                    decimal fruitQty = 1.0m;
                    if (bestFruit.CarbsG > 0)
                    {
                        fruitQty = remaining / bestFruit.CarbsG;
                        fruitQty = Math.Clamp(fruitQty, 0.25m, 1.5m);
                    }

                    decimal pProt = curProt + bestFruit.ProteinG * fruitQty;
                    decimal pCarb = curCarb + bestFruit.CarbsG   * fruitQty;
                    decimal pFat  = curFat  + bestFruit.FatsG    * fruitQty;

                    double newScore = CalcWeightedGap(pProt, pCarb, pFat,
                                                       targetProt, targetCarb, targetFat);

                    if (newScore < gapAfter)
                    {
                        selected.Add((bestFruit, fruitQty));
                        _logger.LogDebug("{Meal}: added fruit {Food} x{Qty}",
                            mealType, bestFruit.FoodName, fruitQty);
                    }
                }
            }
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

            double diff = (double)(cur - target);
            double ratio = Math.Abs(diff) / (double)target;
            
            // Overshoot × 1.5 penalty (extra calories = bad)
            // Undershoot × 1.0 (missing nutrients = less bad)
            if (diff > 0)
                return Math.Min(1.0, ratio * 1.5);
            return Math.Min(1.0, ratio);
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