using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.Linq;
using System.Threading.Tasks;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Collections.Generic;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/progress")]
    public class ProgressController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<ProgressController> _logger;

        public ProgressController(FlexiFitDbContext context, ILogger<ProgressController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetProgressStats([FromQuery] string range = "weekly")
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var startDate = range.ToLower() == "weekly" ? now.AddDays(-7) : now.AddDays(-30);
            int targetDays = range.ToLower() == "weekly" ? 7 : 28;

            try
            {
                // ========== USE DAILY_PROGRESS_LOGS AS PRIMARY SOURCE ==========
                var progressLogs = await _context.DailyProgressLogs
                    .Where(l => l.UserId == userId && l.CreatedAt >= startDate)
                    .OrderBy(l => l.CreatedAt)
                    .ToListAsync();

                // 1. WEIGHT DATA (still from UsrUserMetrics)
                var weightLogs = await _context.UsrUserMetrics
                    .Where(w => w.UserId == userId && w.RecordedAt >= startDate)
                    .OrderBy(w => w.RecordedAt)
                    .Select(w => new ChartEntryDto
                    {
                        Label = w.RecordedAt.ToString("MM/dd"),
                        Value = (float)(w.CurrentWeightKg ?? 0)
                    }).ToListAsync();

                // 2. WORKOUT COMPLETION (from progress logs)
                int completedWorkouts = progressLogs.Count(l => l.CaloriesBurned > 0);
                double compliance = targetDays > 0 ? (double)completedWorkouts / targetDays * 100 : 0;

                // 3. AVERAGE CALORIES (from progress logs)
                int avgCalories = progressLogs.Any()
                    ? (int)progressLogs.Average(l => l.CaloriesBurned ?? 0)
                    : 0;

                // 4. AVERAGE WATER INTAKE (from progress logs)
                double avgWater = progressLogs.Any()
                    ? progressLogs.Average(l => (l.WaterMl ?? 0) / 1000.0)
                    : 0;

                // 5. MEALS COMPLETED (from progress logs)
                int mealsCompleted = progressLogs.Count(l => l.MealPlanCompleted);
                int totalMeals = targetDays; // 1 meal plan per day (contains multiple meals)

                // 6. CALORIE HISTORY (for charts)
                var calorieChartData = progressLogs
                    .GroupBy(l => l.CreatedAt.Date)
                    .Select(g => new ChartEntryDto
                    {
                        Label = g.Key.ToString(range.ToLower() == "weekly" ? "ddd" : "MM/dd"),
                        Value = (float)(g.Sum(l => l.CaloriesBurned ?? 0))
                    })
                    .OrderBy(c => c.Label)
                    .ToList();

                // 7. STREAK CALCULATION (from progress logs)
                var completedDates = progressLogs
                    .Where(l => l.CaloriesBurned > 0)
                    .Select(l => l.CreatedAt.Date)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToList();

                int streak = 0;
                var expectedDate = DateTime.UtcNow.Date;
                foreach (var date in completedDates)
                {
                    if (date == expectedDate)
                    {
                        streak++;
                        expectedDate = expectedDate.AddDays(-1);
                    }
                    else
                    {
                        break;
                    }
                }

                // 8. LATEST WEIGHT & WEIGHT CHANGE
                var latestWeight = await _context.UsrUserMetrics
                    .Where(w => w.UserId == userId)
                    .OrderByDescending(w => w.RecordedAt)
                    .Select(w => w.CurrentWeightKg)
                    .FirstOrDefaultAsync();

                double weightChange = 0;
                if (weightLogs.Count >= 2)
                {
                    weightChange = (double)(weightLogs.Last().Value - weightLogs.First().Value);
                }

                var stats = new ProgressTrackerDto
                {
                    CompliancePercentage = Math.Round(compliance, 1),
                    ComplianceSessions = $"{completedWorkouts} / {targetDays} Sessions",
                    AvgCalories = avgCalories,
                    AvgWaterIntake = Math.Round(avgWater, 1),
                    CurrentStreak = streak,
                    CurrentWeight = (double)(latestWeight ?? 0),
                    WeightHistory = weightLogs,
                    CalorieHistory = calorieChartData,
                    WeightChange = weightChange,
                    MealsCompleted = mealsCompleted,
                    TotalMeals = totalMeals
                };

                _logger.LogInformation($"Progress stats generated for user {userId}, range: {range}");
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting progress stats for user {userId}");
                return StatusCode(500, new { message = "Error fetching progress data", error = ex.Message });
            }
        }

        private int? GetUserId()
        {   
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                    User.FindFirst("user_id")?.Value;
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }
}