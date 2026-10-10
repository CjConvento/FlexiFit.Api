using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlexiFit.Api.Services;

/// <summary>
/// Single source of truth for program day advancement.
/// Item 4: Progression advance = workout DONE + nutrition DONE (GoalMet is NOT a blocker).
/// </summary>
public class ProgramProgressionService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<ProgramProgressionService> _logger;

    public ProgramProgressionService(FlexiFitDbContext db, ILogger<ProgramProgressionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to advance the program day.
    /// Rule: BOTH workout AND nutrition must be "DONE".
    /// GoalMet is a performance indicator ONLY — hindi blocker.
    /// </summary>
    public async Task<bool> TryAdvanceAsync(int userId, DateOnly date)
    {
        _logger.LogInformation("TryAdvanceAsync: userId={UserId}, date={Date}", userId, date);

        // 1. Get daily log for cycle info
        var dailyLog = await _db.NtrDailyLogs
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == date);
        if (dailyLog == null)
        {
            _logger.LogWarning("No daily log found for user {UserId} on {Date}", userId, date);
            return false;
        }

        // 2. Check nutrition completion (status-based, NOT GoalMet)
        var nutritionCalendar = await _db.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == dailyLog.CycleId && c.PlanDate == date);
        bool nutritionDone = nutritionCalendar?.Status == "DONE";

        // 3. Check workout completion (status-based)
        var workoutCalendar = await _db.WktWorkoutCalendars
            .FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == date);
        bool workoutDone = workoutCalendar?.Status == "DONE";

        _logger.LogInformation(
            "Completion check: Nutrition={NutritionDone}, Workout={WorkoutDone}",
            nutritionDone, workoutDone);

        // Item 4: GoalMet is NOT checked here — it's just a performance indicator
        if (!nutritionDone || !workoutDone)
            return false;

        // 4. Get active program
        var activeProgram = await _db.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
        if (activeProgram == null) return false;

        // 5. Race condition guard
        int currentDay = activeProgram.CurrentDayNo;
        var alreadyAdvanced = await _db.DailyProgressLogs
            .AnyAsync(p => p.UserId == userId
                        && p.InstanceId == activeProgram.InstanceId
                        && p.DayNo == currentDay);
        if (alreadyAdvanced)
        {
            _logger.LogWarning("Day {Day} already advanced. Skipping.", currentDay);
            return false;
        }

        // 6. Advance!
        var cycleTarget = await _db.NtrUserCycleTargets
            .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
        int totalDays = (cycleTarget?.WeeksInCycle ?? 4) * 7;

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            activeProgram.CurrentDayNo++;
            if (activeProgram.CurrentDayNo > totalDays)
            {
                activeProgram.Status = "COMPLETED";
                activeProgram.CompletedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // 7. Populate DailyProgressLog
        await PopulateProgressLogAsync(userId, activeProgram, dailyLog, date);

        _logger.LogInformation("Advanced to Day {Day} for user {UserId}",
            activeProgram.CurrentDayNo, userId);
        return true;
    }

    private async Task PopulateProgressLogAsync(
        int userId,
        UsrUserProgramInstance program,
        NtrDailyLog dailyLog,
        DateOnly date)
    {
        int completedDayNo = program.CurrentDayNo - 1;

        // ✅ FIX 1: Gamitin ang ActActivitySummary (Single Source of Truth from Item 1)
        // Imbes na i-recalculate galing sa session workouts, kunin na lang natin ang 
        // actual na na-log na calories para mag-match sa Nutrition Tab.
        int caloriesBurned = await _db.ActActivitySummaries
            .Where(a => a.UserId == userId && a.LogDate == date)
            .SumAsync(a => (int?)a.CaloriesBurned) ?? 0;

        var waterMl = await _db.NtrWaterLogs
            .Where(w => w.UserId == userId && w.LogDate == date)
            .SumAsync(w => (int?)w.WaterMl) ?? 0;

        var progressLog = new DailyProgressLog
        {
            UserId = userId,
            InstanceId = program.InstanceId,
            MonthNo = ((completedDayNo - 1) / 28) + 1,
            WeekNo = ((completedDayNo - 1) / 7) + 1,
            DayNo = completedDayNo,
            CaloriesBurned = caloriesBurned,
            CaloriesIntake = dailyLog.CaloriesConsumed,
            WaterMl = waterMl,
            MealPlanCompleted = true,
            FitnessLevelSnapshot = program.FitnessLevelAtStart ?? "Beginner",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            _db.DailyProgressLogs.Add(progressLog);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            _logger.LogWarning("Duplicate DailyProgressLog for Day {Day}. Skipping.", completedDayNo);
        }
    }
}