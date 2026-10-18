using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Dtos;
using System.Linq;
using FlexiFit.Api.Mappers;

namespace FlexiFit.Api.Services;

public class NutritionService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<NutritionService> _logger;
    private readonly NutritionCalculator _nutritionCalculator;
    private readonly MealPlanGenerator _mealPlanGenerator;
    private readonly ProgramProgressionService _programProgressionService;
    private readonly NutritionTargetService _nutritionTargetService;

    public NutritionService(
        FlexiFitDbContext db,
        ILogger<NutritionService> logger,
        NutritionCalculator nutritionCalculator,
        MealPlanGenerator mealPlanGenerator,
        ProgramProgressionService programProgressionService,
        NutritionTargetService nutritionTargetService)
    {
        _db = db;
        _logger = logger;
        _nutritionCalculator = nutritionCalculator;
        _mealPlanGenerator = mealPlanGenerator;
        _programProgressionService = programProgressionService;
        _nutritionTargetService = nutritionTargetService;
    }

    /// <summary>
    /// Fetches data, performs calculations, and returns the fully mapped NutritionScreenDto.
    /// </summary>
    public async Task<NutritionScreenDto> GetNutritionHistoryDetailAsync(int userId, int day, int month, string baseUrl)
    {
        // Compute targetDate internally from day and month
        var targetDate = new DateOnly(DateTime.UtcNow.Year, month, day);

        // DIRECTLY GET THE LATEST NUTRITION CYCLE (Decoupled from Workout Programs)
        var latestCycle = await _db.NtrUserCycleTargets
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestCycle == null) throw new Exception("No active nutrition cycle found.");

        var calendarDay = await _db.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == latestCycle.CycleId && c.PlanDate == targetDate);
        if (calendarDay == null) throw new Exception($"No nutrition data found for Date {targetDate}");

        var dailyLog = await _db.NtrDailyLogs
            .Include(d => d.NtrDailyMealLogs)
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == targetDate);
        if (dailyLog == null) throw new Exception($"No daily log found for Date {targetDate}");

        // Fetch meal groups (reusing existing method)
        var mealGroups = await GetMealGroupsAsync(dailyLog.DailyLogId, calendarDay.TemplateId, calendarDay.DayNo, calendarDay.VariationCode, baseUrl);

        var target = latestCycle;

        var burnedCalories = await _db.ActActivitySummaries
            .Where(a => a.UserId == userId && a.LogDate == targetDate)
            .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

        var userProfile = await _db.NtrUserNutritionProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);
        string templateName = NutritionCalculator.GetTemplateNameFromDietaryType(userProfile?.DietaryType);

        // ==========================================
        // PREPARE VARIABLES (Calculations happen HERE in the Service)
        // ==========================================
        var (baseTarget, bonusCalories, totalTarget) = await _nutritionTargetService.CalculateAndAdjustTargetAsync(
        userId, targetDate, dailyLog, latestCycle.CycleId);

        double targetProt = (double)(target?.ProteinTargetG ?? 150);

        double consumedProt = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.ProteinG) ?? 0;

        double targetCarbsVal = (double)(target?.CarbsTargetG ?? 200);
        double consumedCarbsVal = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.CarbsG) ?? 0;

        double targetFatsVal = (double)(target?.FatsTargetG ?? 60);
        double consumedFatsVal = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.FatsG) ?? 0;

        int dayNo = calendarDay.DayNo;
        int weekNo = (dayNo - 1) / 7 + 1;

        return NutritionMapper.BuildScreenDto(
            baseTarget, bonusCalories, totalTarget,
            dailyLog.CaloriesConsumed, burnedCalories,
            targetProt, consumedProt,
            targetCarbsVal, consumedCarbsVal,
            targetFatsVal, consumedFatsVal,
            0, 2500, // Water defaults for history
            mealGroups, dailyLog.DailyLogId,
            templateName, dayNo, weekNo
        );
    }

    /// <summary>
    /// Fetches data, performs calculations, and returns the fully mapped DTO.
    /// </summary>
    public async Task<NutritionScreenDto> GetNutritionScreenAsync(int userId, DateOnly targetDate, string baseUrl)
    {
        // DIRECTLY GET THE LATEST NUTRITION CYCLE (Decoupled from Workout Programs)
        var latestCycle = await _db.NtrUserCycleTargets
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestCycle == null) throw new Exception("No active nutrition cycle found.");

        var calendarDay = await _db.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == latestCycle.CycleId && c.PlanDate == targetDate);
        if (calendarDay == null) throw new Exception($"No nutrition data found for Date {targetDate}");

        var dailyLog = await _db.NtrDailyLogs
            .Include(d => d.NtrDailyMealLogs)
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == targetDate);
        if (dailyLog == null) throw new Exception($"No daily log found for Date {targetDate}");

        var target = latestCycle;

        var burnedCalories = await _db.ActActivitySummaries
            .Where(a => a.UserId == userId && a.LogDate == targetDate)
            .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

        var userProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        string templateName = NutritionCalculator.GetTemplateNameFromDietaryType(userProfile?.DietaryType);

        // ==========================================
        // CRITICAL: Check if meals need to be seeded for today
        // ==========================================
        var existingItemCount = await _db.NtrDailyMealItemLogs
            .CountAsync(i => i.DailyLogId == dailyLog.DailyLogId);

        if (existingItemCount == 0)
        {
            await SeedDailyMealsAsync(userId, dailyLog.DailyLogId, dailyLog.TargetNetCalories);
            
            // Reload dailyLog to ensure NtrDailyMealLogs are included after seeding
            dailyLog = await _db.NtrDailyLogs
                .Include(d => d.NtrDailyMealLogs)
                .FirstOrDefaultAsync(l => l.DailyLogId == dailyLog.DailyLogId);
        }

        var mealGroups = await GetMealGroupsAsync(
            dailyLog.DailyLogId, calendarDay.TemplateId, calendarDay.DayNo, 
            calendarDay.VariationCode, baseUrl);

        var waterTotal = await _db.NtrWaterLogs
            .Where(w => w.UserId == userId && w.LogDate == targetDate)
            .SumAsync(w => (int?)w.WaterMl) ?? 0;

        // ==========================================
        // CALCULATIONS HAPPEN HERE (Service Layer)
        // ==========================================
        var (baseTarget, bonusCalories, totalTarget) = await _nutritionTargetService.CalculateAndAdjustTargetAsync(
        userId, targetDate, dailyLog, latestCycle.CycleId);
        
        double consumedCal = dailyLog.CaloriesConsumed;
        double netCalories = consumedCal - burnedCalories;
        double remainingCalories = Math.Max(0, totalTarget - consumedCal);

        double targetProt = (double)(target?.ProteinTargetG ?? 150);
        double consumedProt = dailyLog.NtrDailyMealLogs?.Sum(l => (double)l.ProteinG) ?? 0;

        double targetCarbs = (double)(target?.CarbsTargetG ?? 200);
        double consumedCarbs = dailyLog.NtrDailyMealLogs?.Sum(l => (double)l.CarbsG) ?? 0;

        double targetFats = (double)(target?.FatsTargetG ?? 60);
        double consumedFats = dailyLog.NtrDailyMealLogs?.Sum(l => (double)l.FatsG) ?? 0;

        int dayNo = calendarDay.DayNo;
        int weekNo = (dayNo - 1) / 7 + 1;

        // ==========================================
        // CALL MAPPER (Pure Assignment)
        // ==========================================
        return NutritionMapper.BuildScreenDto(
            baseTarget, bonusCalories, totalTarget,
            consumedCal, burnedCalories,
            targetProt, consumedProt,
            targetCarbs, consumedCarbs,
            targetFats, consumedFats,
            waterTotal, 2500, mealGroups, dailyLog.DailyLogId,
            templateName, dayNo, weekNo
        );
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
                ImageUrl = NutritionMapper.BuildFoodImageUrl(baseUrl, i.Food.ImgFilename),
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

    // ==========================================
    // FOOD DETAILS
    // ==========================================
    public async Task<FoodDetailsResponse> GetFoodDetailsAsync(int foodId, string baseUrl)
    {
        var food = await _db.NtrFoodItems.FirstOrDefaultAsync(f => f.FoodId == foodId);
        if (food == null)
            throw new KeyNotFoundException("Food not found");

        string imgUrl = string.IsNullOrEmpty(food.ImgFilename) || !food.ImgFilename.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}/images/foods/default.png"
            : food.ImgFilename;

        return new FoodDetailsResponse
        {
            FoodId = food.FoodId,
            FoodName = food.FoodName,
            Description = food.Description,
            Calories = (double)food.Calories,
            ProteinG = (double)food.ProteinG,
            CarbsG = (double)food.CarbsG,
            FatsG = (double)food.FatsG,
            ServingUnit = food.ServingUnit,
            ImgFilename = imgUrl
        };
    }

    // ==========================================
    // MEAL ITEM UPDATES
    // ==========================================
    public async Task UpdateMealItemAsync(int mealItemId, int userId, UpdateMealItemRequest request)
    {
        var mealItem = await _db.NtrDailyMealItemLogs
            .Include(m => m.DailyLog)
            .FirstOrDefaultAsync(m => m.ItemLogId == mealItemId);

        if (mealItem == null)
            throw new KeyNotFoundException("Meal item not found");

        if (mealItem.DailyLog.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this meal item");

        mealItem.Qty = request.NewQty;

        var food = await _db.NtrFoodItems.FindAsync(mealItem.FoodId);
        if (food != null)
        {
            mealItem.Calories = food.Calories * request.NewQty;
            mealItem.ProteinG = food.ProteinG * request.NewQty;
            mealItem.CarbsG = food.CarbsG * request.NewQty;
            mealItem.FatsG = food.FatsG * request.NewQty;
        }

        await UpdateDailyMealTotalsAsync(mealItem.DailyLogId);
        await _db.SaveChangesAsync();
    }

    public async Task SwapFoodItemAsync(int mealItemId, int userId, SwapFoodRequest request)
    {
        var currentItem = await _db.NtrDailyMealItemLogs
            .Include(m => m.DailyLog)
            .FirstOrDefaultAsync(m => m.ItemLogId == mealItemId);

        if (currentItem == null)
            throw new KeyNotFoundException("Meal item not found");

        if (currentItem.DailyLog.UserId != userId)
            throw new UnauthorizedAccessException("You do not own this meal item");

        var newFood = await _db.NtrFoodItems.FirstOrDefaultAsync(f => f.FoodId == request.NewFoodId);
        if (newFood == null)
            throw new KeyNotFoundException("New food not found");

        currentItem.FoodId = newFood.FoodId;
        currentItem.Calories = newFood.Calories * currentItem.Qty;
        currentItem.ProteinG = newFood.ProteinG * currentItem.Qty;
        currentItem.CarbsG = newFood.CarbsG * currentItem.Qty;
        currentItem.FatsG = newFood.FatsG * currentItem.Qty;

        await UpdateDailyMealTotalsAsync(currentItem.DailyLogId);
        await _db.SaveChangesAsync();
    }

    // ==========================================
    // WATER INTAKE
    // ==========================================
    public async Task<WaterResponse> AddWaterAsync(int userId, AddWaterRequest request)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var waterLog = await _db.NtrWaterLogs.FirstOrDefaultAsync(w => w.UserId == userId && w.LogDate == today);

        if (waterLog == null)
        {
            waterLog = new NtrWaterLog
            {
                UserId = userId,
                WaterMl = request.AmountMl,
                LogDate = today,
                CreatedAt = DateTime.UtcNow
            };
            _db.NtrWaterLogs.Add(waterLog);
        }
        else
        {
            waterLog.WaterMl += request.AmountMl;
        }

        await _db.SaveChangesAsync();
        return new WaterResponse { WaterMl = waterLog.WaterMl };
    }

    public async Task<WaterResponse> GetWaterTodayAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var waterLog = await _db.NtrWaterLogs.FirstOrDefaultAsync(w => w.UserId == userId && w.LogDate == today);
        return new WaterResponse { WaterMl = waterLog?.WaterMl ?? 0 };
    }

    public async Task ResetWaterAsync(int userId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var waterLog = await _db.NtrWaterLogs.FirstOrDefaultAsync(w => w.UserId == userId && w.LogDate == today);

        if (waterLog != null)
        {
            waterLog.WaterMl = 0;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Updates the daily meal totals based on item logs.
    /// </summary>
    public async Task UpdateDailyMealTotalsAsync(int dailyLogId)
    {
        var dailyLog = await _db.NtrDailyLogs
            .Include(d => d.NtrDailyMealLogs)
            .FirstOrDefaultAsync(d => d.DailyLogId == dailyLogId);

        if (dailyLog == null) return;

        foreach (var mealLog in dailyLog.NtrDailyMealLogs)
        {
            var items = await _db.NtrDailyMealItemLogs
                .Where(i => i.DailyLogId == dailyLogId && i.MealType == mealLog.MealType)
                .ToListAsync();

            mealLog.Calories = (int)items.Sum(i => i.Calories);
            mealLog.ProteinG = items.Sum(i => i.ProteinG);
            mealLog.CarbsG = items.Sum(i => i.CarbsG);
            mealLog.FatsG = items.Sum(i => i.FatsG);
        }

        dailyLog.CaloriesConsumed = dailyLog.NtrDailyMealLogs.Sum(m => m.Calories);
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds daily meals based on user profile and macro targets.
    /// </summary>
    public async Task SeedDailyMealsAsync(int userId, int dailyLogId, decimal targetIntakeCalories)
    {
        _logger.LogInformation("Seeding meals: UserId={UserId}, DailyLogId={DailyLogId}", userId, dailyLogId);

        var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var userProfile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var latestMetric = await _db.UsrUserMetrics.Where(m => m.UserId == userId).OrderByDescending(m => m.RecordedAt).FirstOrDefaultAsync();

        double weight = (double)(latestMetric?.CurrentWeightKg ?? nutProfile?.WeightKg ?? 70m);
        double height = (double)(nutProfile?.HeightCm ?? 170m);
        int age = (int)(nutProfile?.Age ?? 25);
        string gender = userProfile?.Gender ?? "MALE";
        string activityLevel = nutProfile?.ActivityLevel ?? "SEDENTARY";
        string goal = nutProfile?.NutritionGoal ?? "MAINTAIN";
        string dietaryType = nutProfile?.DietaryType ?? "BALANCED";

        var targets = _nutritionCalculator.Calculate(weight, height, age, gender, activityLevel, goal, dietaryType, 0);
        var seedResult = await _mealPlanGenerator.GenerateForDayAsync(userId, dailyLogId, targets);

        if (seedResult.ItemLogs.Any()) _db.NtrDailyMealItemLogs.AddRange(seedResult.ItemLogs);

        foreach (var (mealCode, totals) in seedResult.MealSummaries)
        {
            _db.NtrDailyMealLogs.Add(new NtrDailyMealLog
            {
                DailyLogId = dailyLogId, MealType = mealCode,
                Calories = totals.cal, ProteinG = totals.prot, CarbsG = totals.carb, FatsG = totals.fats
            });
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Handles the complete logic for a nutrition day.
    /// </summary>
    public async Task<object> CompleteNutritionDayAsync(int userId, DateOnly todayDateOnly)
    {
        var dailyLog = await _db.NtrDailyLogs
            .Include(d => d.NtrDailyMealLogs)
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

        if (dailyLog == null) throw new KeyNotFoundException("No daily log found for today.");
        if (dailyLog.MarkedDoneAt != null) return null; // Signal that it's already done

        await UpdateDailyMealTotalsAsync(dailyLog.DailyLogId);
        
        // Reload to get updated totals
        dailyLog = await _db.NtrDailyLogs.Include(d => d.NtrDailyMealLogs).FirstOrDefaultAsync(l => l.DailyLogId == dailyLog.DailyLogId);

        var burnedCalories = await _db.ActActivitySummaries
            .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
            .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

        double net = dailyLog.CaloriesConsumed - burnedCalories;
        double target = dailyLog.TargetNetCalories;

        dailyLog.GoalMet = NutritionCalculator.CheckGoalMet(net, target); 
        dailyLog.MarkedDoneAt = DateTime.UtcNow;

        var nutritionCalendar = await _db.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == dailyLog.CycleId && c.PlanDate == todayDateOnly);
            
        if (nutritionCalendar != null)
        {
            if (nutritionCalendar.Status == "DONE") throw new InvalidOperationException("Nutrition day already marked as DONE.");
            nutritionCalendar.Status = "DONE";
            nutritionCalendar.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await _programProgressionService.TryAdvanceAsync(userId, todayDateOnly);

        return new
        {
            message = "Nutrition day completed successfully! 🍎",
            currentDay = nutritionCalendar?.DayNo ?? 1,
            isCompleted = true,
            summary = new
            {
                targetCalories = target,
                consumedCalories = dailyLog.CaloriesConsumed,
                burnedCalories = burnedCalories,
                netCalories = net,
                goalMet = dailyLog.GoalMet
            }
        };
    }
}