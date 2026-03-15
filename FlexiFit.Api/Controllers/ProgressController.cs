using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.Linq;
using System.Threading.Tasks;
using FlexiFit.Api.Entities; // Ito ang namespace ng DbContext mo
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Collections.Generic;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/workout")]
    public class ProgressController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly IConfiguration _configuration;

        public ProgressController(FlexiFitDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetProgressStats([FromQuery] string range = "weekly")
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();
            int userId = int.Parse(userIdClaim);

            var now = DateTime.UtcNow;
            var startDate = range == "weekly" ? now.AddDays(-7) : now.AddDays(-30);

            try
            {
                // 1. WEIGHT DATA (UsrUserMetric.cs - RecordedAt & CurrentWeightKg)
                var weightLogs = await _context.UsrUserMetrics
                    .Where(w => w.UserId == userId && w.RecordedAt >= startDate)
                    .OrderBy(w => w.RecordedAt)
                    .Select(w => new ChartEntryDto
                    {
                        Label = w.RecordedAt.ToString("ddd"),
                        Value = (float)(w.CurrentWeightKg ?? 0)
                    }).ToListAsync();

                // 2. CALORIE DATA (Mula sa DailyProgressLog.cs - CreatedAt & CaloriesBurned)
                var dailyLogs = await _context.DailyProgressLogs
                    .Where(w => w.UserId == userId && w.CreatedAt >= startDate)
                    .ToListAsync();

                var calorieChartData = dailyLogs
                    .GroupBy(w => range == "weekly" ? w.CreatedAt.DayOfWeek.ToString() : "W" + ((w.CreatedAt.Day - 1) / 7 + 1))
                    .Select(g => new ChartEntryDto
                    {
                        Label = g.Key.Length > 3 ? g.Key.Substring(0, 3) : g.Key,
                        Value = (float)g.Sum(w => w.CaloriesBurned ?? 0)
                    }).ToList();

                // 3. WATER INTAKE (NtrWaterLog.cs - WaterMl & LogDate)
                // Note: Ang LogDate ay DateOnly, kaya convert natin sa DateTime para sa comparison
                var avgWater = await _context.NtrWaterLogs
                    .Where(w => w.UserId == userId)
                    .ToListAsync(); // Load for processing due to DateOnly limitations in some EF versions

                var filteredWater = avgWater
                    .Where(w => w.LogDate.ToDateTime(TimeOnly.MinValue) >= startDate)
                    .Select(w => (double)w.WaterMl / 1000.0) // Convert ML to Liters
                    .DefaultIfEmpty(0.0)
                    .Average();

                // 4. WORKOUT SESSIONS (UsrUserWorkoutSession.cs - UserId & Status)
                var completedSessions = await _context.UsrUserWorkoutSessions
                    .Where(w => w.UserId == userId && w.Status == "COMPLETED" && w.CreatedAt >= startDate)
                    .CountAsync();

                int targetWorkouts = range == "weekly" ? 7 : 30;
                double compliance = (double)completedSessions / targetWorkouts * 100;

                // 5. STREAK LOGIC
                var streakDates = dailyLogs
                    .OrderByDescending(w => w.CreatedAt)
                    .Select(w => w.CreatedAt.Date)
                    .Distinct()
                    .ToList();

                int streak = 0;
                var currentDay = DateTime.UtcNow.Date;
                foreach (var date in streakDates)
                {
                    if (date == currentDay.AddDays(-streak)) streak++;
                    else break;
                }

                var latestWeight = await _context.UsrUserMetrics
                    .Where(w => w.UserId == userId)
                    .OrderByDescending(w => w.RecordedAt)
                    .Select(w => w.CurrentWeightKg)
                    .FirstOrDefaultAsync();

                var stats = new ProgressTrackerDto
                {
                    CompliancePercentage = Math.Round(compliance, 1),
                    ComplianceSessions = $"{completedSessions} / {targetWorkouts} Sessions",
                    AvgCalories = calorieChartData.Any() ? (int)calorieChartData.Average(c => c.Value) : 0,
                    AvgWaterIntake = Math.Round(filteredWater, 1),
                    CurrentStreak = streak,
                    CurrentWeight = (double)(latestWeight ?? 0),
                    WeightHistory = weightLogs,
                    CalorieHistory = calorieChartData,
                    WeightChange = weightLogs.Count >= 2 ? (double)(weightLogs.Last().Value - weightLogs.First().Value) : 0.0,
                    MealsCompleted = dailyLogs.Count(l => l.MealPlanCompleted),
                    TotalMeals = targetWorkouts * 3 // Assumption: 3 meals a day
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}