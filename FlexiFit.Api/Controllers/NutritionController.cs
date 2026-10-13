using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FlexiFit.Api.Mappers;
using FlexiFit.Api.Extensions;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/nutrition")]
public class NutritionController : ControllerBase
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<NutritionController> _logger;
    private readonly NutritionCalculator _nutritionCalculator;
    private readonly NutritionService _nutritionService;
    private readonly MealPlanGenerator _mealPlanGenerator;
    private readonly ProgramProgressionService _programProgressionService;
    private readonly NutritionTargetService _nutritionTargetService;

    public NutritionController(
        FlexiFitDbContext db, 
        ILogger<NutritionController> logger,
        NutritionCalculator nutritionCalculator,
        MealPlanGenerator mealPlanGenerator,
        ProgramProgressionService programProgressionService,
        NutritionTargetService nutritionTargetService,
        NutritionService nutritionService)
    {
        _db = db;
        _logger = logger;
        _nutritionCalculator = nutritionCalculator;
        _mealPlanGenerator = mealPlanGenerator;
        _programProgressionService = programProgressionService;
        _nutritionTargetService = nutritionTargetService;
        _nutritionService = nutritionService;
    }

    //  GET TODAY'S NUTRITION PLAN
    [HttpGet("today")]
    public async Task<IActionResult> GetTodayPlan()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            // 1. Get Active Program
            var activeProgram = await _db.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return NotFound(new { message = "No active program found." });

            // 2. Compute week and day in week (1-7)
            int dayNumber = activeProgram.CurrentDayNo;
            int weekNo = (dayNumber - 1) / 7 + 1;
            int dayInWeek = (dayNumber - 1) % 7 + 1;

            // 3. Get variation code based on week
            string variationCode = NutritionCalculator.GetVariationCode(weekNo);

            // 4. Get user's dietary type and corresponding template ID
            var userProfile = await _db.NtrUserNutritionProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId);
            int templateId = NutritionCalculator.GetTemplateIdFromDietaryType(userProfile?.DietaryType);

            // 5. Get or Create Calendar Entry for today
            var calendarDay = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == activeProgram.CycleNo
                                       && c.PlanDate == todayDateOnly);

            if (calendarDay == null)
            {
                calendarDay = new NtrMealPlanCalendar
                {
                    CycleId = activeProgram.CycleNo,
                    PlanDate = todayDateOnly,
                    WeekNo = weekNo,
                    DayNo = dayInWeek,
                    TemplateId = templateId,
                    VariationCode = variationCode,
                    IsWorkoutDay = true,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.NtrMealPlanCalendars.Add(calendarDay);
                await _db.SaveChangesAsync();
            }

            // 6. Get User Targets
            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            decimal targetNetCal = target?.DailyTargetNetCalories ?? 2000;

            // 7. Get Daily Log (Kailangan ito bago tawagin ang NutritionTargetService)
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
                    TargetNetCalories = (int)targetNetCal,
                    CaloriesConsumed = 0,
                    CaloriesBurned = 0,
                    GoalMet = false
                };
                _db.NtrDailyLogs.Add(dailyLog);
                await _db.SaveChangesAsync();
                await SeedDailyMeals(userId.Value, dailyLog.DailyLogId, dailyLog.TargetNetCalories);
                dailyLog = await _db.NtrDailyLogs.Include(d => d.NtrDailyMealLogs)
                    .FirstOrDefaultAsync(l => l.DailyLogId == dailyLog.DailyLogId);
            }
            else
            {   
                // Existing calendar mismatch and seeding logic
                var existingItemCount = await _db.NtrDailyMealItemLogs.CountAsync(i => i.DailyLogId == dailyLog.DailyLogId);

                // Check kung nag-advance na ang araw para i-reseed ang meals kung wala pang items
                if (existingItemCount > 0 && calendarDay.DayNo != dayInWeek)
                {
                    _logger.LogInformation($"Calendar day mismatch: stored={calendarDay.DayNo}, current={dayInWeek}. Reseeding meals.");
                    var oldItems = await _db.NtrDailyMealItemLogs.Where(i => i.DailyLogId == dailyLog.DailyLogId).ToListAsync();
                    _db.NtrDailyMealItemLogs.RemoveRange(oldItems);

                    var oldMealLogs = await _db.NtrDailyMealLogs.Where(m => m.DailyLogId == dailyLog.DailyLogId).ToListAsync();
                    _db.NtrDailyMealLogs.RemoveRange(oldMealLogs);
                    await _db.SaveChangesAsync();
                
                    calendarDay.DayNo = dayInWeek;
                    await _db.SaveChangesAsync();
                    existingItemCount = 0; // force seeding
                }

                if(existingItemCount == 0)
                {
                    await SeedDailyMeals(userId.Value, dailyLog.DailyLogId, dailyLog.TargetNetCalories);   
                    dailyLog = await _db.NtrDailyLogs.Include(d => d.NtrDailyMealLogs)
                        .FirstOrDefaultAsync(l => l.DailyLogId == dailyLog.DailyLogId); 
                }
            }

            // 8. Calling NutritionTargetService to get Base + Bonus = Total
            var (baseTarget, bonusCalories, totalTarget) = 
                await _nutritionTargetService.CalculateAndAdjustTargetAsync(
                    userId.Value, todayDateOnly, dailyLog, activeProgram.CycleNo);

            // 9. Get Today's Meals (using actual logged items)
            var mealGroups = await _nutritionService.GetMealGroupsAsync(dailyLog!.DailyLogId, calendarDay.TemplateId,
                                                 dayInWeek, variationCode, baseUrl);

            // 10. Get Water Intake
            var waterTotal = await _db.NtrWaterLogs
                .Where(w => w.UserId == userId && w.LogDate == todayDateOnly)
                .SumAsync(w => (int?)w.WaterMl) ?? 0;

            // 11. Map to DTO (gumamit ng values na nanggaling sa NutritionTargetService para consistent sa Base + Bonus = Total)
            double consumedCal = dailyLog?.CaloriesConsumed ?? 0;
            string templateName = NutritionCalculator.GetTemplateNameFromDietaryType(userProfile?.DietaryType);

            return Ok(new NutritionScreenDto
            {
                BaseTarget = baseTarget,
                BonusCalories = bonusCalories,
                TotalTarget = totalTarget,
                TargetCalories = totalTarget,
                
                ConsumedCalories = consumedCal,
                BurnedCalories = bonusCalories,
                NetCalories = consumedCal - bonusCalories,

                RemainingCalories = Math.Max(0, totalTarget - consumedCal),

                TargetProtein = (double)(target?.ProteinTargetG ?? 150),
                ConsumedProtein = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.ProteinG) ?? 0,
                TargetCarbs = (double)(target?.CarbsTargetG ?? 200),
                ConsumedCarbs = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.CarbsG) ?? 0,
                TargetFats = (double)(target?.FatsTargetG ?? 60),
                ConsumedFats = dailyLog?.NtrDailyMealLogs?.Sum(l => (double)l.FatsG) ?? 0,

                WaterConsumedMl = waterTotal,
                WaterTargetMl = 2500,
                Meals = mealGroups,
                DailyLogId = dailyLog!.DailyLogId,
                TemplateName = templateName,   

                // Day Properties
                DayNo = calendarDay.DayNo,
                WeekNo = weekNo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today's nutrition plan");
            return StatusCode(500, new { message = "Error fetching nutrition plan" });
        }
    }

    [HttpGet("history-detail")]
    public async Task<IActionResult> GetNutritionHistoryDetail([FromQuery] int day, [FromQuery] int month)
    {
        var userId = User.GetUserId();
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

            // Check if meal items exist; if not, seed them
            var existingItemCount = await _db.NtrDailyMealItemLogs
                .CountAsync(i => i.DailyLogId == dailyLog.DailyLogId);

            if (existingItemCount == 0)
            {
                await SeedDailyMeals(userId.Value, dailyLog.DailyLogId,
                                     dailyLog.TargetNetCalories);
            }

            //  Pass variationCode and baseUrl
            var mealGroups = await _nutritionService.GetMealGroupsAsync(dailyLog.DailyLogId, calendarDay.TemplateId, calendarDay.DayNo,
                                                 calendarDay.VariationCode, baseUrl);

            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == targetDate)
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            double consumedCal = dailyLog?.CaloriesConsumed ?? 0;

            //  Fetch user profile to get dietary type
            var userProfile = await _db.NtrUserNutritionProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId);
            string templateName = NutritionCalculator.GetTemplateNameFromDietaryType(userProfile?.DietaryType);

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
                DailyLogId = dailyLog!.DailyLogId,
                TemplateName = templateName,   // <-- ADD THIS

                // Day Properties
                DayNo = calendarDay.DayNo,
                WeekNo = (calendarDay.DayNo - 1) / 7 + 1 // Calculate week number based on day number
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

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            //  Resolve image URL: Appwrite full URL passthrough, else placeholder
            string imgUrl;
            if (string.IsNullOrEmpty(food.ImgFilename))
            {
                imgUrl = $"{baseUrl}/images/foods/default.png";
            }
            else if (food.ImgFilename.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                imgUrl = food.ImgFilename;
            }
            else
            {
                // Legacy bare filename → fallback to placeholder
                imgUrl = $"{baseUrl}/images/foods/default.png";
            }

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
                ImgFilename = imgUrl
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
        var userId = User.GetUserId();
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
        var userId = User.GetUserId();
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

    /// <summary>
    /// Add water intake for today
    /// </summary>
    [HttpPost("water/add")]
    public async Task<IActionResult> AddWater([FromBody] AddWaterRequest request)
    {
        var userId = User.GetUserId();
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
        var userId = User.GetUserId();
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
        var userId = User.GetUserId();
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


    [HttpPost("complete")]
    public async Task<IActionResult> CompleteNutritionDay([FromBody] LogFullDayRequest req)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        try
        {
            var dailyLog = await _db.NtrDailyLogs
                .Include(d => d.NtrDailyMealLogs)
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

            if (dailyLog == null)
                return NotFound(new { message = "No daily log found for today." });

            // Guard: prevent completing a day that is already marked as done
            if (dailyLog.MarkedDoneAt != null)
            {
                return BadRequest(new { message = "Nutrition for today has already been completed." });
            }

            // Recalculate totals from meal items
            await UpdateDailyMealTotals(dailyLog.DailyLogId);
            dailyLog = await _db.NtrDailyLogs
                .Include(d => d.NtrDailyMealLogs)
                .FirstOrDefaultAsync(l => l.DailyLogId == dailyLog.DailyLogId);

            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            double net = dailyLog!.CaloriesConsumed - burnedCalories;
            double target = dailyLog.TargetNetCalories;

            // 1. Gamitin ang centralized calculator (Item 4: Performance indicator lang)
            dailyLog.GoalMet = NutritionCalculator.CheckGoalMet(net, target); 
            dailyLog.MarkedDoneAt = DateTime.UtcNow;

            var nutritionCalendar = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == dailyLog.CycleId && c.PlanDate == todayDateOnly);
            if (nutritionCalendar != null)
            {
                // Guard: prevent updating calendar if it's already DONE
                if (nutritionCalendar.Status == "DONE")
                    return BadRequest(new { message = "Nutrition day already marked as DONE." });

                nutritionCalendar.Status = "DONE";
                nutritionCalendar.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            // 2. Gamitin ang single source of truth service para sa advancement
            await _programProgressionService.TryAdvanceAsync(userId.Value, todayDateOnly);

            return Ok(new
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
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing nutrition day please complete workout first");
            return StatusCode(500, new { message = "Error completing nutrition day please complete workout first" });
        }
    }

    #region Admin CRUD Endpoints

    /// <summary>
    /// GET: api/nutrition/admin/foods
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [HttpGet("admin/foods")]
    public async Task<ActionResult<object>> AdminGetAllFoods(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null)
    {
        try
        {
            //  Validate inputs
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100; // Max limit para iwas abuse

            _logger.LogInformation("📡 ADMIN: Fetching foods — Page {Page}, Size {PageSize}, Search: {Search}", page, pageSize, search);
            
            //  Base query
            var query = _db.NtrFoodItems.AsNoTracking().AsQueryable();

            //  Optional search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(f =>
                    f.FoodName.ToLower().Contains(lowerSearch) ||
                    f.MealType!.ToLower().Contains(lowerSearch) ||
                    f.DietaryType!.ToLower().Contains(lowerSearch) ||
                    f.Category!.ToLower().Contains(lowerSearch));
            }

            //  Get total count BEFORE pagination
            var totalCount = await query.CountAsync();

            //  Apply pagination + sorting
            var foods = await query
                .OrderBy(f => f.FoodName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            _logger.LogInformation(" ADMIN: Retrieved {Count} foods (Page {Page}/{TotalPages}, Total: {Total})",
            foods.Count, page, totalPages, totalCount);

            //  Return paginated response
            return Ok(new
            {
                data = foods,
                total = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = totalPages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching foods");
            return StatusCode(500, new { error = "An error occurred while fetching foods." });
        }
    }
    
    /// <summary>
    /// GET: api/nutrition/admin/foods/{id}
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [HttpGet("admin/foods/{id}")]
    public async Task<ActionResult<NtrFoodItem>> AdminGetFood(int id)
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Fetching food ID: {Id}", id);
            var food = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == id);

            if (food == null)
            {
                _logger.LogWarning("❌ ADMIN: Food not found: {Id}", id);
                return NotFound(new { error = $"Food with ID {id} not found." });
            }

            return Ok(food);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching food ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the food." });
        }
    }

    /// <summary>
    /// POST: api/nutrition/admin/foods
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [HttpPost("admin/foods")]
    public async Task<ActionResult<NtrFoodItem>> AdminCreateFood([FromBody] NtrFoodItem food)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ ADMIN: Invalid model state for food creation");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("📡 ADMIN: Creating new food: {Name}", food.FoodName);

            food.CreatedAt = DateTime.UtcNow;
            food.UpdatedAt = DateTime.UtcNow;
            food.IsActive = true;

            _db.NtrFoodItems.Add(food);
            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Food created with ID: {Id}", food.FoodId);
            return CreatedAtAction(nameof(AdminGetFood), new { id = food.FoodId }, food);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error creating food: {Name}", food.FoodName);
            return StatusCode(500, new { error = "An error occurred while creating the food." });
        }
    }

    /// <summary>
    /// PUT: api/nutrition/admin/foods/{id}
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [HttpPut("admin/foods/{id}")]
    public async Task<IActionResult> AdminUpdateFood(int id, [FromBody] NtrFoodItem food)
    {
        try
        {
            if (id != food.FoodId)
            {
                _logger.LogWarning("❌ ADMIN: ID mismatch: {Id} vs {FoodId}", id, food.FoodId);
                return BadRequest(new { error = "ID mismatch." });
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ ADMIN: Invalid model state for food update");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("📡 ADMIN: Updating food ID: {Id}", id);

            var existing = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == id);

            if (existing == null)
            {
                _logger.LogWarning("❌ ADMIN: Food not found: {Id}", id);
                return NotFound(new { error = $"Food with ID {id} not found." });
            }

            // Update fields
            existing.FoodName = food.FoodName;
            existing.MealType = food.MealType;
            existing.DietaryType = food.DietaryType;
            existing.Category = food.Category;
            existing.SizeType = food.SizeType;
            existing.ServingUnit = food.ServingUnit;
            existing.ServingWeightG = food.ServingWeightG;
            existing.Calories = food.Calories;
            existing.ProteinG = food.ProteinG;
            existing.CarbsG = food.CarbsG;
            existing.FatsG = food.FatsG;
            existing.IsActive = food.IsActive;
            existing.ImgFilename = food.ImgFilename;
            existing.Description = food.Description;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Food updated: {Id}", id);
            return Ok(new { message = "Food updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error updating food ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while updating the food." });
        }
    }

    /// <summary>
    /// DELETE: api/nutrition/admin/foods/{id}
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [HttpDelete("admin/foods/{id}")]
    public async Task<IActionResult> AdminDeleteFood(int id)
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Deleting food ID: {Id}", id);

            var food = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == id);

            if (food == null)
            {
                _logger.LogWarning("❌ ADMIN: Food not found: {Id}", id);
                return NotFound(new { error = $"Food with ID {id} not found." });
            }

            _db.NtrFoodItems.Remove(food);
            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Food deleted: {Id}", id);
            return Ok(new { message = "Food deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error deleting food ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the food." });
        }
    }

    #endregion

    #region Helper Methods

    private async Task SeedDailyMeals(int userId, int dailyLogId, decimal targetIntakeCalories)
    {
        _logger.LogInformation("Seeding meals via MealPlanGenerator: UserId={UserId}, DailyLogId={DailyLogId}, Target={Target}", userId, dailyLogId, targetIntakeCalories);

        // 1. Get user profile to calculate accurate macros
        var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var userProfile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId); //  Dito ang Gender
        var latestMetric = await _db.UsrUserMetrics
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.RecordedAt)
            .FirstOrDefaultAsync();
        var user = await _db.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);

        // Fallbacks if profile/metric is missing
        double weight = (double)(latestMetric?.CurrentWeightKg ?? nutProfile?.WeightKg ?? 70m);
        double height = (double)(nutProfile?.HeightCm ?? 170m);
        int age = (int)(nutProfile?.Age ?? 25);
        string gender = userProfile?.Gender ?? "MALE";
        string activityLevel = nutProfile?.ActivityLevel ?? "SEDENTARY";
        string goal = nutProfile?.NutritionGoal ?? "MAINTAIN";
        string dietaryType = nutProfile?.DietaryType ?? "BALANCED";

        // 2. Calculate Macro Targets (workoutCaloriesBurned = 0 for baseline seeding)
        var targets = _nutritionCalculator.Calculate(
            weightKg: weight,
            heightCm: height,
            age: age,
            gender: gender,
            activityLevel: activityLevel,
            goal: goal,
            dietaryType: dietaryType,
            workoutCaloriesBurned: 0
        );

        // 3. Generate Meals using the new Macro-Aware Generator
        var seedResult = await _mealPlanGenerator.GenerateForDayAsync(userId, dailyLogId, targets);

        if (seedResult.ItemLogs.Any())
        {
            _db.NtrDailyMealItemLogs.AddRange(seedResult.ItemLogs);
        }

        foreach (var (mealCode, totals) in seedResult.MealSummaries)
        {
            _db.NtrDailyMealLogs.Add(new NtrDailyMealLog
            {
                DailyLogId = dailyLogId,
                MealType = mealCode,
                Calories = totals.cal,
                ProteinG = totals.prot,
                CarbsG = totals.carb,
                FatsG = totals.fats
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Successfully seeded {ItemCount} meal items for DailyLog {DailyLogId}", seedResult.ItemLogs.Count, dailyLogId);
    }

    #endregion
}