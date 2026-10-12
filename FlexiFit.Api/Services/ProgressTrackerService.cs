using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class ProgressTrackerService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<ProgressTrackerService> _logger;

    public ProgressTrackerService(FlexiFitDbContext db, ILogger<ProgressTrackerService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ProgressTrackerDto> GetStatsAsync(int userId, string range)
    {
        var now = DateTime.UtcNow;
        var startDate = range.ToLower() == "weekly" ? now.AddDays(-7) : now.AddDays(-30);
        int targetDays = range.ToLower() == "weekly" ? 7 : 28;
        var startDateOnly = DateOnly.FromDateTime(startDate);
        var endDateOnly = DateOnly.FromDateTime(now);

        try
        {
            // 1. WEIGHT DATA (still from UsrUserMetrics)
            var weightLogs = await _db.UsrUserMetrics
                .Where(w => w.UserId == userId && w.RecordedAt >= startDate)
                .OrderBy(w => w.RecordedAt)
                .Select(w => new ChartEntryDto
                {
                    Label = w.RecordedAt.ToString("MM/dd"),                        
                    Value = (float)(w.CurrentWeightKg ?? 0)
                }).ToListAsync();
            
            // Latest weight for current weight and weight change calculation
            var latestWeight = await _db.UsrUserMetrics
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.RecordedAt)
                .Select(w => w.CurrentWeightKg)                    
                .FirstOrDefaultAsync();

            double weightChange = weightLogs.Count >= 2
                ? (double)(weightLogs.Last().Value - weightLogs.First().Value)
                : 0;

            // 2. NUTRITION & ACTIVITY DATA (Gamit ang NtrDailyLogs and ActActivitySummary)
           var dailyLogs = await _db.NtrDailyLogs
                .Where(l => l.UserId == userId && l.PlanDate >= startDateOnly && l.PlanDate <= endDateOnly)
                .ToListAsync();

            var activitySummaries = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate >= startDateOnly && a.LogDate <= endDateOnly)
                .ToListAsync();

            // 3. WORKOUT COMPLETION (gamit ang wktworkoutcalendars)
            var workoutCalendars = await _db.WktWorkoutCalendars
                .Where(c => c.UserId == userId && c.PlanDate >= startDateOnly && c.PlanDate <= endDateOnly && c.IsWorkoutDay == true)
                .ToListAsync();

            // --- Calculations ---
            int completedWorkouts = workoutCalendars.Count(c => c.Status == "DONE");
            double compliance = targetDays > 0 ? (double)completedWorkouts / targetDays * 100 : 0;

            int totalDaysLogged = dailyLogs.Count;
            double totalTarget = dailyLogs.Sum(l => l.TargetNetCalories); // ✅ Includes Base + Burn (Item 3)
            double totalConsumed = dailyLogs.Sum(l => l.CaloriesConsumed);
            double totalBurned = activitySummaries.Sum(a => a.CaloriesBurned);
            int daysGoalMet = dailyLogs.Count(l => l.GoalMet);

            double avgTarget = totalDaysLogged > 0 ? totalTarget / totalDaysLogged : 0;
            double avgConsumed = totalDaysLogged > 0 ? totalConsumed / totalDaysLogged : 0;
            double avgBurned = totalDaysLogged > 0 ? totalBurned / totalDaysLogged : 0;
            double avgNet = avgConsumed - avgBurned;
            double adherence = totalTarget > 0 ? (totalConsumed / totalTarget) * 100 : 0;
            double avgCalorieDiff = totalDaysLogged > 0 ? dailyLogs.Average(l => l.TargetNetCalories - l.CaloriesConsumed) : 0;

            // 4. STREAK CALCULATION (from wktworkoutcalendars)
            var completedDates = workoutCalendars
                .Where(c => c.Status == "DONE")
                .Select(c => c.PlanDate)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            int streak = 0;
            var expectedDate = DateOnly.FromDateTime(DateTime.UtcNow);

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

            // 5. CALORIE HISTORY (for charts)
            var calorieChartData = dailyLogs
                .GroupBy(l => l.PlanDate)
                .Select(g => new ChartEntryDto
                {
                    Label = g.Key.ToString(range.ToLower() == "weekly" ? "ddd" : "MM/dd"),
                    Value = (float)(g.Sum(l => l.CaloriesConsumed))
                })
                .OrderBy(c => c.Label)
                .ToList();

            // Map to DTO
            return new ProgressTrackerDto
            {
                CompliancePercentage = Math.Round(compliance, 1),
                ComplianceSessions = $"{completedWorkouts} / {targetDays} Sessions",
                AvgCalories = (int)Math.Round(avgConsumed),
                AvgWaterIntake = 8, // Placeholder, calculate based on water logs if water intake is tracked
                CurrentStreak = streak,
                CurrentWeight = (double)(latestWeight ?? 0),
                WeightHistory = weightLogs,
                CalorieHistory = calorieChartData,
                WeightChange = Math.Round(weightChange, 2),
                MealsCompleted = dailyLogs.Count(l => l.CaloriesConsumed > 0),
                TotalMeals = totalDaysLogged,

                // Advanced Nutrition Stats
                AverageTargetCalories = Math.Round(avgTarget, 2),
                AverageConsumedCalories = Math.Round(avgConsumed, 2),
                AverageBurnedCalories = Math.Round(avgBurned, 2),
                AverageNetCalories = Math.Round(avgNet, 2),
                TotalDaysLogged = totalDaysLogged,
                DaysGoalMet = daysGoalMet,
                NutritionAdherencePercentage = Math.Round(adherence, 2),
                AverageCalorieDifference = Math.Round(avgCalorieDiff, 2)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error calculating progress stats for user {userId}");
            throw; //Let the controller handle the HTTP response
        }
    }
}