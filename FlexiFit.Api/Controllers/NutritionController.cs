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
    private readonly ILogger<NutritionController> _logger;
    private readonly NutritionService _nutritionService;

    public NutritionController(
        ILogger<NutritionController> logger,
        NutritionService nutritionService)
    {
        _logger = logger;
        _nutritionService = nutritionService;
    }

    //  GET TODAY'S NUTRITION PLAN
    [HttpGet("today")]
    public async Task<IActionResult> GetTodayPlan()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // Ang Service na ang nag-fetch, nag-compute at Mapper ang nag-map ng DTO para sa GetTodayPlan
        var response = await _nutritionService.GetNutritionScreenAsync(userId.Value, todayDateOnly, baseUrl);

        // Gumagamit na ng Global Exception Handling Middleware, kaya hindi na kailangan ng try-catch dito
        return Ok(response);
    }

    [HttpGet("history-detail")]
    public async Task<IActionResult> GetNutritionHistoryDetail([FromQuery] int day, [FromQuery] int month)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // Ang Service na ang nag-fetch, nag-compute at Mapper nag-map ng DTO para sa GetNutritionHistoryDetail
        var response = await _nutritionService.GetNutritionHistoryDetailAsync(userId.Value, day, month, baseUrl);

        return Ok(response);
    }

    /// <summary>
    /// Get food details by ID
    /// </summary>
    [HttpGet("food/{foodId}")]
    public async Task<IActionResult> GetFoodDetails(int foodId)
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var response = await _nutritionService.GetFoodDetailsAsync(foodId, baseUrl);
        return Ok(response);
    }

    /// <summary>
    /// Update meal item quantity
    /// </summary>
    [HttpPut("meal-item/{mealItemId}")]
    public async Task<IActionResult> UpdateMealItem(int mealItemId, [FromBody] UpdateMealItemRequest request)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        await _nutritionService.UpdateMealItemAsync(mealItemId, userId.Value, request);
        return Ok(new { message = "Meal item updated successfully" });
    }

    /// <summary>
    /// Swap food item with another food
    /// </summary>
    [HttpPost("meal-item/{mealItemId}/swap")]
    public async Task<IActionResult> SwapFoodItem(int mealItemId, [FromBody] SwapFoodRequest request)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        await _nutritionService.SwapFoodItemAsync(mealItemId, userId.Value, request);
        return Ok(new { message = "Food swapped successfully" });
    }

    /// <summary>
    /// Add water intake for today
    /// </summary>
    [HttpPost("water/add")]
    public async Task<IActionResult> AddWater([FromBody] AddWaterRequest request)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        var response = await _nutritionService.AddWaterAsync(userId.Value, request);
        return Ok(response);

    }

    /// <summary>
    /// Get today's water intake
    /// </summary>
    [HttpGet("water/today")]
    public async Task<IActionResult> GetWaterToday()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        var response = await _nutritionService.GetWaterTodayAsync(userId.Value);
        return Ok(response);
    }

    /// <summary>
    /// Reset today's water intake
    /// </summary>
    [HttpDelete("water/reset")]
    public async Task<IActionResult> ResetWater()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        await _nutritionService.ResetWaterAsync(userId.Value);
        return Ok(new { message = "Water reset successfully" });
    }


    [HttpPost("complete")]
    public async Task<IActionResult> CompleteNutritionDay([FromBody] LogFullDayRequest req)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        // DELEGATE ALL BUSINESS LOGIC TO SERVICE
        var result = await _nutritionService.CompleteNutritionDayAsync(userId.Value, todayDateOnly);

        return Ok(result);

    }
}