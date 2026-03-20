using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/nutrition")]
public class NutritionController : ControllerBase
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<NutritionController> _logger;

    public NutritionController(FlexiFitDbContext db, ILogger<NutritionController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ✅ GET TODAY'S NUTRITION PLAN
    [HttpGet("today")]
    public async Task<IActionResult> GetTodayPlan()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            // 1. Get Active Program
            var activeProgram = await _db.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

<<<<<<< HEAD
            if (activeProgram == null)
                return NotFound(new { message = "No active program found." });

            // 2. Get or Create Calendar Entry for today
            var calendarDay = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == activeProgram.CycleNo
                                       && c.PlanDate == todayDateOnly);

            if (calendarDay == null)
            {
                // Create calendar entry if doesn't exist
                calendarDay = new NtrMealPlanCalendar
                {
                    CycleId = activeProgram.CycleNo,
                    PlanDate = todayDateOnly,
                    WeekNo = ((activeProgram.CurrentDayNo - 1) / 7) + 1,
                    DayNo = activeProgram.CurrentDayNo,
                    TemplateId = 1,
                    VariationCode = "A",
                    IsWorkoutDay = true,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.NtrMealPlanCalendars.Add(calendarDay);
                await _db.SaveChangesAsync();
            }

            // 3. Get User Targets
            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            // 4. Get Burned Calories
            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            // 5. Get Daily Log
            var dailyLog = await _db.NtrDailyLogs
                .Include(d => d.NtrDailyMealLogs)
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

            if (dailyLog == null)
            {
                dailyLog = new NtrDailyLog
                {
                    UserId = userId.Value,
                    CycleId = activeProgram.CycleNo,
                    PlanDate = todayDateOnly,
                    TargetNetCalories = target?.DailyTargetNetCalories ?? 2000,
                    CaloriesConsumed = 0,
                    CaloriesBurned = 0,
                    GoalMet = false
                };
                _db.NtrDailyLogs.Add(dailyLog);
                await _db.SaveChangesAsync();

                // Seed meals for today
                await SeedDailyMeals(dailyLog.DailyLogId, calendarDay.TemplateId, calendarDay.DayNo);
            }

            // 6. Get Today's Meals
            var mealGroups = await GetMealGroups(dailyLog.DailyLogId, calendarDay.TemplateId, calendarDay.DayNo, baseUrl);

            // 7. Get Water Intake
=======
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            // 1. 🔥 THE FIX: Join sa Cycle at i-check ang status sa Calendar table
            // Base sa screenshot mo, ang 'status' ay nasa ntr_meal_plan_calendar
            var calendarDay = await _db.NtrMealPlanCalendars
                .Include(c => c.Cycle)
                .FirstOrDefaultAsync(c =>
                    c.Cycle.UserId == userId &&
                    c.PlanDate == todayDateOnly &&
                    c.Status == "PENDING"); // 💡 O gamitin ang 'ACTIVE' kung binabago mo ito

            if (calendarDay == null) return NotFound(new { message = "No active meal plan found for today." });

            // 2. Kunin ang latest user targets
            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            // 3. Engine: Calories Burned
            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            // 4. 🔥 REVISED QUERY: Fetch Meals and Top 2 Food Items
            var mealGroupsRaw = await _db.NtrTemplateDayMeals
                .Where(tdm => tdm.TemplateDayId == calendarDay.TemplateId)
                .Select(tdm => new
                {
                    tdm.TemplateMealId,
                    tdm.MealType,
                    // 💡 Siguraduhin na Top 2 lang per meal type para sa Dashboard
                    Foods = _db.NtrTemplateMealItems
                        .Where(tmi => tmi.TemplateMealId == tdm.TemplateMealId)
                        .Join(_db.NtrFoodItems,
                              tmi => tmi.FoodId,
                              food => food.FoodId,
                              (tmi, food) => new { tmi, food })
                        .OrderBy(x => x.food.FoodName) // Consistent ordering
                        .Take(2) // 👈 ANTI-BAHA: Top 2 items lang
                        .Select(x => new
                        {
                            x.food.FoodId,
                            x.food.FoodName,
                            x.food.Description,
                            x.food.ImgFilename,
                            x.food.DietaryType,
                            x.food.ServingUnit,
                            x.tmi.DefaultQty,
                            x.food.Calories,
                            x.food.ProteinG,
                            x.food.CarbsG,
                            x.food.FatsG
                        }).ToList()
                }).ToListAsync();

            // 5. Fetch Logs for Status
            var todayLogs = await _db.NtrDailyMealLogs
                .Include(l => l.DailyLog)
                .Where(l => l.DailyLog.UserId == userId && l.DailyLog.PlanDate == todayDateOnly)
                .ToListAsync();

            // 6. Mapping to DTO (Safe for Android)
            var mealGroups = mealGroupsRaw
                .OrderBy(g => g.MealType == "B" ? 1 : g.MealType == "L" ? 2 : g.MealType == "S" ? 3 : 4)
                .Select(group => new MealGroupDto
                {
                    TemplateMealId = group.TemplateMealId,
                    MealType = group.MealType, // I-retain ang "B", "L", etc. para sa logic ng Android
                    Status = todayLogs.Any(l => l.MealType == group.MealType) ? "DONE" : "PENDING",
                    FoodItems = group.Foods.Select(f => new FoodItemDto
                    {
                        FoodId = f.FoodId,
                        Name = f.FoodName,
                        Description = f.Description ?? "",
                        Unit = f.ServingUnit,
                        Qty = (double)f.DefaultQty,
                        Calories = (double)f.Calories,
                        Protein = (double)f.ProteinG,
                        Carbs = (double)f.CarbsG,
                        Fats = (double)f.FatsG,
                        DietaryType = f.DietaryType ?? "balanced",
                        // 💡 Gamit ang baseUrl para sa full path
                        ImageUrl = BuildFoodImageUrl(baseUrl, f.DietaryType ?? "balanced", group.MealType, f.ImgFilename)
                    }).ToList()
                }).ToList();

            // 7. Water intake
>>>>>>> a8456a38043692fdfc40a22fb1f9845660c78f0f
            var waterTotal = await _db.NtrWaterLogs
                .Where(w => w.UserId == userId && w.LogDate == todayDateOnly)
                .SumAsync(w => (int?)w.WaterMl) ?? 0;

<<<<<<< HEAD
            // 8. Calculate totals
            double targetCal = (double)(target?.DailyTargetNetCalories ?? 2000);
            double consumedCal = dailyLog?.CaloriesConsumed ?? 0;
=======
            // 8. Calculations
            double targetCal = (double)(target?.DailyTargetNetCalories ?? 2000);
            double consumedCal = todayLogs.Sum(l => (double)l.Calories);
>>>>>>> a8456a38043692fdfc40a22fb1f9845660c78f0f

            return Ok(new NutritionScreenDto
            {
                TargetCalories = targetCal,
                ConsumedCalories = consumedCal,
                BurnedCalories = burnedCalories,
<<<<<<< HEAD
                NetCalories = consumedCal - burnedCalories,
                RemainingCalories = Math.Max(0, targetCal - (consumedCal - burnedCalories)),

                TargetProtein = (double)(target?.ProteinTargetG ?? 150),
                ConsumedProtein = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.ProteinG) ?? 0,
                TargetCarbs = (double)(target?.CarbsTargetG ?? 200),
                ConsumedCarbs = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.CarbsG) ?? 0,
                TargetFats = (double)(target?.FatsTargetG ?? 60),
                ConsumedFats = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.FatsG) ?? 0,

                WaterConsumedMl = waterTotal,
                WaterTargetMl = 2500,
                Meals = mealGroups,
                DailyLogId = dailyLog.DailyLogId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today's nutrition plan");
            return StatusCode(500, new { message = "Error fetching nutrition plan" });
        }
    }

    // ✅ GET NUTRITION BY DATE (for calendar history)
    [HttpGet("history-detail")]
    public async Task<IActionResult> GetNutritionHistoryDetail([FromQuery] int day, [FromQuery] int month)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var targetDate = new DateOnly(DateTime.UtcNow.Year, month, day);

        try
        {
            var activeProgram = await _db.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return NotFound(new { message = "No active program found." });

            var calendarDay = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == activeProgram.CycleNo && c.PlanDate == targetDate);

            if (calendarDay == null)
                return NotFound(new { message = $"No nutrition data found for Day {day}" });

            var dailyLog = await _db.NtrDailyLogs
                .Include(d => d.NtrDailyMealLogs)
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == targetDate);

            if (dailyLog == null)
                return NotFound(new { message = $"No daily log found for Day {day}" });

            var mealGroups = await GetMealGroups(dailyLog.DailyLogId, calendarDay.TemplateId, calendarDay.DayNo, baseUrl);

            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == targetDate)
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            double consumedCal = dailyLog?.CaloriesConsumed ?? 0;

            return Ok(new NutritionScreenDto
            {
                TargetCalories = (double)(target?.DailyTargetNetCalories ?? 2000),
                ConsumedCalories = consumedCal,
                BurnedCalories = burnedCalories,
                NetCalories = consumedCal - burnedCalories,
                RemainingCalories = Math.Max(0, (target?.DailyTargetNetCalories ?? 2000) - (consumedCal - burnedCalories)),

                TargetProtein = (double)(target?.ProteinTargetG ?? 150),
                ConsumedProtein = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.ProteinG) ?? 0,
                TargetCarbs = (double)(target?.CarbsTargetG ?? 200),
                ConsumedCarbs = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.CarbsG) ?? 0,
                TargetFats = (double)(target?.FatsTargetG ?? 60),
                ConsumedFats = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.FatsG) ?? 0,

                WaterConsumedMl = 0,
                WaterTargetMl = 2500,
                Meals = mealGroups,
                DailyLogId = dailyLog.DailyLogId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting nutrition for day {day}");
            return StatusCode(500, new { message = "Error fetching nutrition details" });
        }
    }

    /// <summary>
    /// Get food details by ID
    /// </summary>
    [HttpGet("food/{foodId}")]
    public async Task<IActionResult> GetFoodDetails(int foodId)
    {
        try
        {
            var food = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == foodId);

            if (food == null)
                return NotFound(new { message = "Food not found" });

            return Ok(new FoodDetailsResponse
            {
                FoodId = food.FoodId,
                FoodName = food.FoodName,
                Description = food.Description,
                Calories = (double)food.Calories,
                ProteinG = (double)food.ProteinG,
                CarbsG = (double)food.CarbsG,
                FatsG = (double)food.FatsG,
                ServingUnit = food.ServingUnit,
                ImgFilename = food.ImgFilename
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting food details for ID {foodId}");
            return StatusCode(500, new { message = "Error getting food details" });
        }
    }

    /// <summary>
    /// Update meal item quantity
    /// </summary>
    [HttpPut("meal-item/{mealItemId}")]
    public async Task<IActionResult> UpdateMealItem(int mealItemId, [FromBody] UpdateMealItemRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var mealItem = await _db.NtrDailyMealItemLogs
                .Include(m => m.DailyLog)
                .FirstOrDefaultAsync(m => m.ItemLogId == mealItemId);

            if (mealItem == null)
                return NotFound(new { message = "Meal item not found" });

            if (mealItem.DailyLog.UserId != userId)
                return Forbid();

            // Update quantity
            mealItem.Qty = request.NewQty;

            // Recalculate macros based on new quantity
            var food = await _db.NtrFoodItems.FindAsync(mealItem.FoodId);
            if (food != null)
            {
                mealItem.Calories = food.Calories * request.NewQty;
                mealItem.ProteinG = food.ProteinG * request.NewQty;
                mealItem.CarbsG = food.CarbsG * request.NewQty;
                mealItem.FatsG = food.FatsG * request.NewQty;
            }

            // Update daily meal totals
            await UpdateDailyMealTotals(mealItem.DailyLogId);

            await _db.SaveChangesAsync();

            _logger.LogInformation($"User {userId} updated meal item {mealItemId} to quantity {request.NewQty}");

            return Ok(new { message = "Meal item updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating meal item {mealItemId}");
            return StatusCode(500, new { message = "Error updating meal item" });
        }
    }

    /// <summary>
    /// Swap food item with another food
    /// </summary>
    [HttpPost("meal-item/{mealItemId}/swap")]
    public async Task<IActionResult> SwapFoodItem(int mealItemId, [FromBody] SwapFoodRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var currentItem = await _db.NtrDailyMealItemLogs
                .Include(m => m.DailyLog)
                .FirstOrDefaultAsync(m => m.ItemLogId == mealItemId);

            if (currentItem == null)
                return NotFound(new { message = "Meal item not found" });

            if (currentItem.DailyLog.UserId != userId)
                return Forbid();

            var newFood = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == request.NewFoodId);

            if (newFood == null)
                return NotFound(new { message = "New food not found" });

            // Update to new food
            currentItem.FoodId = newFood.FoodId;
            currentItem.Calories = newFood.Calories * currentItem.Qty;
            currentItem.ProteinG = newFood.ProteinG * currentItem.Qty;
            currentItem.CarbsG = newFood.CarbsG * currentItem.Qty;
            currentItem.FatsG = newFood.FatsG * currentItem.Qty;

            // Update daily meal totals
            await UpdateDailyMealTotals(currentItem.DailyLogId);

            await _db.SaveChangesAsync();

            _logger.LogInformation($"User {userId} swapped meal item {mealItemId} to food {request.NewFoodId}");

            return Ok(new { message = "Food swapped successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error swapping food for meal item {mealItemId}");
            return StatusCode(500, new { message = "Error swapping food" });
        }
    }

    // Helper method to update daily meal totals
    private async Task UpdateDailyMealTotals(int dailyLogId)
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
    }

    // ✅ COMPLETE NUTRITION DAY
    [HttpPost("complete")]

    // Add these methods to NutritionController.cs

    /// <summary>
    /// Add water intake for today
    /// </summary>
    [HttpPost("water/add")]
    public async Task<IActionResult> AddWater([FromBody] AddWaterRequest request)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var waterLog = await _db.NtrWaterLogs
                .FirstOrDefaultAsync(w => w.UserId == userId && w.LogDate == today);

            if (waterLog == null)
            {
                waterLog = new NtrWaterLog
                {
                    UserId = userId.Value,
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

            _logger.LogInformation($"User {userId} added {request.AmountMl}ml water. Total: {waterLog.WaterMl}ml");

            return Ok(new WaterResponse { WaterMl = waterLog.WaterMl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error adding water for user {userId}");
            return StatusCode(500, new { message = "Error adding water intake" });
        }
    }

    /// <summary>
    /// Get today's water intake
    /// </summary>
    [HttpGet("water/today")]
    public async Task<IActionResult> GetWaterToday()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var waterLog = await _db.NtrWaterLogs
                .FirstOrDefaultAsync(w => w.UserId == userId && w.LogDate == today);

            var waterMl = waterLog?.WaterMl ?? 0;

            return Ok(new WaterResponse { WaterMl = waterMl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting water for user {userId}");
            return StatusCode(500, new { message = "Error getting water intake" });
        }
    }

    /// <summary>
    /// Reset today's water intake
    /// </summary>
    [HttpDelete("water/reset")]
    public async Task<IActionResult> ResetWater()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var waterLog = await _db.NtrWaterLogs
                .FirstOrDefaultAsync(w => w.UserId == userId && w.LogDate == today);

            if (waterLog != null)
            {
                waterLog.WaterMl = 0;
                await _db.SaveChangesAsync();
            }

            _logger.LogInformation($"User {userId} reset water intake");

            return Ok(new { message = "Water reset successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error resetting water for user {userId}");
            return StatusCode(500, new { message = "Error resetting water intake" });
        }
    }

    public async Task<IActionResult> CompleteNutritionDay([FromBody] LogFullDayRequest req)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var dailyLog = await _db.NtrDailyLogs
                .Include(d => d.NtrDailyMealLogs)
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

            if (dailyLog == null)
                return NotFound(new { message = "No daily log found for today." });

            // Update meal logs
            foreach (var meal in req.Meals)
            {
                var mealLog = dailyLog.NtrDailyMealLogs
                    .FirstOrDefault(m => m.MealType == meal.MealType);

                if (mealLog != null)
                {
                    mealLog.Calories = (int)meal.TotalCalories;
                    mealLog.ProteinG = (decimal)meal.TotalProtein;
                    mealLog.CarbsG = (decimal)meal.TotalCarbs;
                    mealLog.FatsG = (decimal)meal.TotalFats;
                }
            }

            // Update daily totals
            dailyLog.CaloriesConsumed = dailyLog.NtrDailyMealLogs.Sum(m => m.Calories);
            dailyLog.MarkedDoneAt = DateTime.UtcNow;

            // Update calendar status
            var calendarDay = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == req.CycleId && c.PlanDate == todayDateOnly);

            if (calendarDay != null)
            {
                calendarDay.Status = "DONE";
                calendarDay.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Ok(new NutritionCompleteResultDto
            {
                Message = "Nutrition day completed successfully! 🍎",
                CurrentDay = calendarDay?.DayNo ?? 1,
                IsCompleted = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing nutrition day");
            return StatusCode(500, new { message = "Error completing nutrition day" });
        }
    }

    #region Helper Methods

    private async Task SeedDailyMeals(int dailyLogId, int templateId, int dayNo)
    {
        var templateDay = await _db.NtrTemplateDays
            .Include(td => td.NtrTemplateDayMeals)
                .ThenInclude(tdm => tdm.NtrTemplateMealItems)
                    .ThenInclude(tmi => tmi.Food)
            .FirstOrDefaultAsync(td => td.TemplateId == templateId && td.DayNo == dayNo);

        if (templateDay == null) return;

        foreach (var tdm in templateDay.NtrTemplateDayMeals)
        {
            decimal totalCal = 0, totalProt = 0, totalCarbs = 0, totalFats = 0;
            var itemsToLog = new List<NtrDailyMealItemLog>();

            foreach (var item in tdm.NtrTemplateMealItems.Where(i => !i.IsOptionalAddon))
            {
                if (item.Food == null) continue;

                var qty = (decimal)item.DefaultQty;
                var cal = item.Food.Calories * qty;
                var prot = item.Food.ProteinG * qty;
                var carb = item.Food.CarbsG * qty;
                var fat = item.Food.FatsG * qty;

                itemsToLog.Add(new NtrDailyMealItemLog
                {
                    DailyLogId = dailyLogId,
                    MealType = tdm.MealType,
                    FoodId = item.FoodId,
                    Qty = item.DefaultQty,
                    IsAddon = item.IsOptionalAddon,
                    Calories = cal,
                    ProteinG = prot,
                    CarbsG = carb,
                    FatsG = fat,
                    SortOrder = item.SortOrder
                });

                totalCal += cal;
                totalProt += prot;
                totalCarbs += carb;
                totalFats += fat;
            }

            var mealLog = new NtrDailyMealLog
            {
                DailyLogId = dailyLogId,
                MealType = tdm.MealType,
                Calories = (int)totalCal,
                ProteinG = totalProt,
                CarbsG = totalCarbs,
                FatsG = totalFats
            };

            _db.NtrDailyMealLogs.Add(mealLog);
            _db.NtrDailyMealItemLogs.AddRange(itemsToLog);
        }

        await _db.SaveChangesAsync();
    }

    private async Task<List<MealGroupDto>> GetMealGroups(int dailyLogId, int templateId, int dayNo, string baseUrl)
    {
        var mealGroups = new List<MealGroupDto>();

        var templateMeals = await _db.NtrTemplateDayMeals
            .Include(tdm => tdm.NtrTemplateMealItems)
                .ThenInclude(tmi => tmi.Food)
            .Where(tdm => tdm.TemplateDay.TemplateId == templateId && tdm.TemplateDay.DayNo == dayNo)
            .ToListAsync();

        var mealLogs = await _db.NtrDailyMealLogs
            .Where(ml => ml.DailyLogId == dailyLogId)
            .ToDictionaryAsync(ml => ml.MealType);

        foreach (var tdm in templateMeals.OrderBy(m => GetMealOrder(m.MealType)))
        {
            var mealLog = mealLogs.GetValueOrDefault(tdm.MealType);

            var foodItems = tdm.NtrTemplateMealItems
                .Where(i => !i.IsOptionalAddon)
                .Select(i => new FoodItemDto
                {
                    FoodId = i.FoodId,
                    Name = i.Food.FoodName,
                    Description = i.Food.Description ?? "",
                    ImageUrl = BuildFoodImageUrl(baseUrl, i.Food.DietaryType ?? "balanced", tdm.MealType, i.Food.ImgFilename),
                    DietaryType = i.Food.DietaryType ?? "balanced",
                    Qty = (double)i.DefaultQty,
                    Unit = i.Food.ServingUnit,
                    Calories = (double)i.Food.Calories,
                    Protein = (double)i.Food.ProteinG,
                    Carbs = (double)i.Food.CarbsG,
                    Fats = (double)i.Food.FatsG
                })
                .Take(2) // Show first 2 items in dashboard
                .ToList();

            mealGroups.Add(new MealGroupDto
            {
                TemplateMealId = tdm.TemplateMealId,
                MealType = tdm.MealType,
                Status = mealLog != null ? "DONE" : "PENDING",
                FoodItems = foodItems
            });
        }

        return mealGroups;
    }

    private int GetMealOrder(string mealType)
    {
        return mealType switch
        {
            "B" => 1,
            "L" => 2,
            "S" => 3,
            "D" => 4,
            _ => 5
        };
    }

    private string BuildFoodImageUrl(string baseUrl, string category, string mealType, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return $"{baseUrl}/images/foods/default.png";

        string typeFolder = mealType.ToUpper() switch
        {
            "B" => "breakfast",
            "L" => "lunch",
            "S" => "snacks",
            "D" => "dinner",
            _ => "general"
        };

        return $"{baseUrl}/images/foods/{typeFolder}/{fileName}";
    }

    private int? GetUserId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("user_id")?.Value;
        return int.TryParse(id, out var userId) ? userId : null;
    }

    #endregion
=======
                RemainingCalories = Math.Max(0, targetCal - (consumedCal - burnedCalories)),

                // Macro Totals (Huwag kalimutan i-map ito, babe!)
                TargetProtein = (double)(target?.ProteinTargetG ?? 150),
                ConsumedProtein = todayLogs.Sum(l => (double)l.ProteinG),
                TargetCarbs = (double)(target?.CarbsTargetG ?? 200),
                ConsumedCarbs = todayLogs.Sum(l => (double)l.CarbsG),
                TargetFats = (double)(target?.FatsTargetG ?? 60),
                ConsumedFats = todayLogs.Sum(l => (double)l.FatsG),

                WaterConsumedMl = waterTotal,
                WaterTargetMl = 2500,
                Meals = mealGroups
            });
        }

        // 💡 Helper revised with BaseURL
        private string BuildFoodImageUrl(string baseUrl, string category, string mealType, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return $"{baseUrl}/images/foods/default.png";

            string catFolder = category.ToLower().Trim().Replace(" ", "_");
            string typeFolder = mealType.ToUpper() switch
            {
                "B" => "breakfast",
                "L" => "lunch",
                "S" => "snacks",
                "D" => "dinner",
                _ => "general"
            };

            return $"{baseUrl}/images/foods/{catFolder}/{typeFolder}/{fileName}";
        }


        [HttpPost("log-full-day")]
        public async Task<IActionResult> LogFullDay([FromBody] LogFullDayRequest req)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            // 1. Siguraduhin na may Daily Log record na para sa araw na ito
            var dailyLog = await _db.NtrDailyLogs
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

            if (dailyLog == null)
            {
                dailyLog = new NtrDailyLog
                {
                    UserId = userId.Value,
                    PlanDate = todayDateOnly,
                    CycleId = req.CycleId
                };
                _db.NtrDailyLogs.Add(dailyLog);
                await _db.SaveChangesAsync();
            }

            // 2. Loop sa bawat meal na pinasa (B, L, S, D)
            foreach (var m in req.Meals)
            {
                var mealLog = await _db.NtrDailyMealLogs
                    .FirstOrDefaultAsync(ml => ml.DailyLogId == dailyLog.DailyLogId && ml.MealType == m.MealType);

                if (mealLog == null)
                {
                    _db.NtrDailyMealLogs.Add(new NtrDailyMealLog
                    {
                        DailyLogId = dailyLog.DailyLogId,
                        MealType = m.MealType,
                        Calories = (int)m.TotalCalories,
                        ProteinG = (decimal)m.TotalProtein,
                        CarbsG = (decimal)m.TotalCarbs,
                        FatsG = (decimal)m.TotalFats
                    });
                }
                else
                {
                    mealLog.Calories = (int)m.TotalCalories;
                    mealLog.ProteinG = (decimal)m.TotalProtein;
                    mealLog.CarbsG = (decimal)m.TotalCarbs;
                    mealLog.FatsG = (decimal)m.TotalFats;
                }
            }

            // 3. 🔥 DITO NA PAPASOK ANG PAG-UPDATE NG CALENDAR STATUS
            var calendarRecord = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == req.CycleId && c.PlanDate == todayDateOnly);

            if (calendarRecord != null)
            {
                calendarRecord.Status = "DONE"; // <--- Mula PENDING, magiging COMPLETED na!
                calendarRecord.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "All meals logged and Calendar updated to COMPLETED!" });
        }

        private int? GetUserId()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

>>>>>>> a8456a38043692fdfc40a22fb1f9845660c78f0f
}