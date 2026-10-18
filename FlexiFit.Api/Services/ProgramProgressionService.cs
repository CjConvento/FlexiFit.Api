using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace FlexiFit.Api.Services;

/// <summary>
/// Single source of truth for program day advancement.
/// Item 4: Progression advance = workout DONE + nutrition DONE (GoalMet is NOT a blocker).
/// </summary>
public class ProgramProgressionService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<ProgramProgressionService> _logger;
    private readonly ProgramService _programService;

    public ProgramProgressionService(
        FlexiFitDbContext db, 
        ILogger<ProgramProgressionService> logger, 
        ProgramService programService)
    {
        _db = db;
        _logger = logger;
        _programService = programService;
    }

    /// <summary>
    /// Attempts to advance program day(s) for a date.
    /// Rule: BOTH workout AND nutrition must be "DONE".
    /// GoalMet is a performance indicator ONLY — hindi blocker.
    /// </summary>
    //  <param name="instanceId">
    /// The program that was just triggered/completed. Only that program is checked and advanced.
    /// When null (legacy callers), every program with a DONE workout row for the date is advanced;
    /// rows without an InstanceId fall back to the first ACTIVE program.
    /// </param>

    public async Task<bool> TryAdvanceAsync(int userId, DateOnly date, int? instanceId = null)
    {
        _logger.LogInformation("TryAdvanceAsync: userId={UserId}, date={Date}, instanceId={InstanceId}", userId, date, instanceId);
        
        // 1. Get daily log for cycle info
        var dailyLog = await _db.NtrDailyLogs
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == date);
        if (dailyLog == null)
        {
            _logger.LogWarning("No daily log found for user {UserId} on {Date}", userId, date);
            return false;
        }

        // 2. Check nutrition completion (status-based, NOT GoalMet) - date level, shared by all programs
        var nutritionCalendar = await _db.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == dailyLog.CycleId && c.PlanDate == date);
        bool nutritionDone = nutritionCalendar?.Status == "DONE";

        if (!nutritionDone)
        {
            _logger.LogInformation("Nutrition not DONE yet for {Date}", date);
            return false;
        }

        // 3. Which program instances have a DONE workout row for this date?
        var doneRows = await _db.WktWorkoutCalendars
            .AsNoTracking()
            .Where(w => w.UserId == userId && w.PlanDate == date && (w.Status == "DONE" || w.Status == "SKIPPED")
                        && (instanceId == null || w.InstanceId == instanceId))
            .Select(w => w.InstanceId)
            .ToListAsync();

        if (doneRows.Count == 0)
        {
            _logger.LogInformation("No DONE workout row for {Date} (instanceId={InstanceId})", date, instanceId);
            return false;
        }

        var targetIds = doneRows.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();


       // Legacy rows (no InstanceId) -> first ACTIVE program, same as the old behaviour
        if (instanceId == null && doneRows.Any(i => !i.HasValue))
        {
            var legacyId = await _db.UsrUserProgramInstances
                .Where(p => p.UserId == userId && p.Status == "ACTIVE")
                .OrderBy(p => p.CreatedAt).ThenBy(p => p.InstanceId)
                .Select(p => (int?)p.InstanceId)
                .FirstOrDefaultAsync();
            if (legacyId.HasValue && !targetIds.Contains(legacyId.Value))
                targetIds.Add(legacyId.Value);
        }

        bool anyAdvanced = false;
        foreach (var id in targetIds)
        {
            if (await AdvanceInstanceAsync(userId, id, dailyLog, date))
                anyAdvanced = true;
        }

        return anyAdvanced;
    }

    /// <summary>Advances ONE program instance by one day (race guard + transaction + progress log).</summary>
    private async Task<bool> AdvanceInstanceAsync(int userId, int instanceId, NtrDailyLog dailyLog, DateOnly date)
    {
        var activeProgram = await _db.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.InstanceId == instanceId && p.Status == "ACTIVE");
        if (activeProgram == null) return false;

        // Race condition guard
        int currentDay = activeProgram.CurrentDayNo;
        var alreadyAdvanced = await _db.DailyProgressLogs
            .AnyAsync(p => p.UserId == userId
                        && p.InstanceId == activeProgram.InstanceId
                        && p.DayNo == currentDay);
        if (alreadyAdvanced)
        {
            _logger.LogWarning("Instance {InstanceId} day {Day} already advanced. Skipping.", instanceId, currentDay);
            return false;
        }

        var cycleTarget = await _db.NtrUserCycleTargets
            .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        int totalDays = ProgramConstants.DaysPerCycle; // call from ProgramConstants instead of hardcoding 28

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

        await PopulateProgressLogAsync(userId, activeProgram, dailyLog, date);

        // AUTO LEVEL-UP: Trigger level-up immediately upon completion
        if (activeProgram.Status == "COMPLETED")
        {
            _logger.LogInformation("Program {InstanceId} completed. Triggering auto level-up.", instanceId);
            await _programService.CheckProgressionAsync(userId, instanceId);
        }

        return true;
    }


    private async Task PopulateProgressLogAsync(
        int userId,
        UsrUserProgramInstance program,
        NtrDailyLog dailyLog,
        DateOnly date)
    {
        int completedDayNo = program.CurrentDayNo - 1;

        // Gamitin ang ActActivitySummary (Single Source of Truth from Item 1)
        // Imbes na i-recalculate galing sa session workouts, kunin na lang natin ang 
        // actual na na-log na calories para mag-match sa Nutrition Tab.
        int caloriesBurned = await _db.ActActivitySummaries
            .Where(a => a.UserId == userId && a.LogDate == date)
            .SumAsync(a => (int?)a.CaloriesBurned) ?? 0;

        var waterMl = await _db.NtrWaterLogs
            .Where(w => w.UserId == userId && w.LogDate == date)
            .SumAsync(w => (int?)w.WaterMl) ?? 0;

        // HYBRID LOGGING LOGIC
        // Determine if the program is continuous based on the initial fitness level.
        bool isContinuous = program.FitnessLevelAtStart?.ToUpper() == "ADVANCED" || 
                            program.FitnessLevelAtStart?.ToUpper() == "REHAB LEVEL";

        int yearNo = 1;
        int monthNo = 1;

        if (isContinuous)
        {
            // Math for continuous tracking (up to 10+ years)
            yearNo = ((completedDayNo - 1) / ProgramConstants.DaysPerYear) + 1;
            int dayOfYear = (completedDayNo - 1) % ProgramConstants.DaysPerYear;
            monthNo = (dayOfYear / 28) + 1;
        }


        var progressLog = new DailyProgressLog
        {
            UserId = userId,
            InstanceId = program.InstanceId,
            YearNo = yearNo,
            MonthNo = monthNo, // DYNAMIC BASED ON CONTINUOUS LOGIC
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