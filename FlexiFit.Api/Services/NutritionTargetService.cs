using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

/// <summary>
/// Single Source of Truth para sa Target Adjustment.
/// Responsable sa pag-compute ng Base + Bonus = Total, at pag-a-update ng DB.
/// </summary>
public class NutritionTargetService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<NutritionTargetService> _logger;
    public NutritionTargetService(
        FlexiFitDbContext db,
        ILogger<NutritionTargetService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Nagre-return ng breakdown (Base, Bonus, Total) at nag-a-adjust ng DB kung kailangan.
    /// </summary>
    public async Task<(double BaseTarget, double BonusCalories, double TotalTarget)>
    CalculateAndAdjustTargetAsync(int userId, DateOnly date, NtrDailyLog dailyLog, int cycleId)
    {
        // 1. Get Base Target (From Cycle Targets)
        var target = await _db.NtrUserCycleTargets
            .Where(t => t.UserId == userId && t.CycleId == cycleId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        double baseTarget = target?.DailyTargetNetCalories ?? 2000; // fallback kung walang target sa DB

        // 2. Get Burned Calories (From ActivitySummaries)
        double burnedCalories = await _db.ActActivitySummaries
            .Where(a => a.UserId == userId && a.LogDate == date)
            .SumAsync(a => (double?)a.CaloriesBurned) ?? 0; // fallback kung walang activity summary sa DB

        // 3. Compute Total Target (Base + Bonus)
        double totalTarget = baseTarget + burnedCalories;

        // 4. Idempotency Check: I-update lang ang DB kung hindi pa "Done" ang day
        if (dailyLog.MarkedDoneAt == null)
        {
            // Update lang kung nagbago ang value (iwas sa unnecessary writes)
            if (Math.Abs((double)dailyLog.TargetNetCalories - totalTarget) > 0.01)
            {
                dailyLog.TargetNetCalories = (int)totalTarget;
                await _db.SaveChangesAsync();
                _logger.LogInformation($"Adjusted target for user {userId}: {baseTarget} + {burnedCalories} = {totalTarget}");
            }
        }
        else
        {
            // If day is already "Done", use the historical stored targets (Base + Bonus = Total) for consistency   
            totalTarget = dailyLog.TargetNetCalories;
        }
        return (baseTarget, burnedCalories, totalTarget);
    }
}