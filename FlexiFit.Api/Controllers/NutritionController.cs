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

        try
        {
            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Ang Service na ang nag-fetch, nag-compute at Mapper ang nag-map ng DTO para sa GetTodayPlan
            var response = await _nutritionService.GetNutritionScreenAsync(userId.Value, todayDateOnly, baseUrl);

            return Ok(response);
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

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Ang Service na ang nag-fetch, nag-compute at Mapper nag-map ng DTO para sa GetNutritionHistoryDetail
            var response = await _nutritionService.GetNutritionHistoryDetailAsync(userId.Value, day, month, baseUrl);

            return Ok(response);
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
            await _nutritionService.UpdateDailyMealTotalsAsync(mealItem.DailyLogId);

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
            await _nutritionService.UpdateDailyMealTotalsAsync(currentItem.DailyLogId);

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

        try
        {
            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
            
            // DELEGATE ALL BUSINESS LOGIC TO SERVICE
            var result = await _nutritionService.CompleteNutritionDayAsync(userId.Value, todayDateOnly);
            
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing nutrition day please complete workout first");
            return StatusCode(500, new { message = "Error completing nutrition day please complete workout first" });
        }
    }
}