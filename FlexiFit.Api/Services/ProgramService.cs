using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class ProgramService
{
    private readonly FlexiFitDbContext _context;
    private readonly WorkoutPoolSelector _poolSelector;

    // CONSTRUCTOR
    public ProgramService(
        FlexiFitDbContext context, 
        WorkoutPoolSelector poolSelector)
    {
        _context = context;
        _poolSelector = poolSelector;
    }
    // ==========================================
    // PROGRAM TRACKING LOGIC 
    // ==========================================

    /// <summary>
    /// Gets all ACTIVE program instances for a user, ordered by creation date.
    /// </summary>
    public async Task<List<UsrUserProgramInstance>> GetActiveProgramsAsync(int userId)
    {
        return await _context.UsrUserProgramInstances
            .Where(p => p.UserId == userId && p.Status == "ACTIVE")
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.InstanceId)
            .Include(p => p.Program)
            .ToListAsync();
    }

    /// <summary>
    /// Calculates the 1-based ordinal number of a specific program instance.
    /// </summary>
    public async Task<int> GetProgramNumberAsync(int userId, int instanceId)
    {
        var activePrograms = await GetActiveProgramsAsync(userId);
        int index = activePrograms.FindIndex(p => p.InstanceId == instanceId);
        return index >= 0 ? index + 1 : 1;
    }

    /// <summary>
    /// Gets the total count of ACTIVE programs for a user.
    /// </summary>
    public async Task<int> GetTotalActiveProgramsAsync(int userId)
    {
        return await _context.UsrUserProgramInstances
            .CountAsync(p => p.UserId == userId && p.Status == "ACTIVE");
    }

    // ==========================================
    // PROGRESSION LOGIC 
    // ==========================================
    
    /// <summary>
    /// Checks progression for a specific program (multi-program safe).
    /// </summary>
    public async Task<object> CheckProgressionAsync(int userId, int? instanceId = null)
    {
        UsrUserProgramInstance? program;

        if (instanceId.HasValue)
        {
            program = await _context.UsrUserProgramInstances
                .Include(p => p.Program)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.InstanceId == instanceId.Value);
            
            if (program == null) 
                throw new KeyNotFoundException($"Program instance {instanceId.Value} not found.");
        }
        else
        {
            // Legacy fallback: gets the first active program
            program = await _context.UsrUserProgramInstances
                .Include(p => p.Program)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
            
            if (program == null) 
                throw new KeyNotFoundException("No active program found.");
        }

        return await ProcessProgressionLogicAsync(program);
    }

    /// <summary>
    /// ADMIN CHEAT ENGINE: Advances the program by exactly one full cycle (28 days).
    /// This guarantees crossing the threshold to trigger HandleLevelUpAsync.
    /// </summary>
    public async Task<object> FastForwardToEndAsync(int userId, int? instanceId = null)
    {
        UsrUserProgramInstance? program;
        
        if (instanceId.HasValue)
        {
            program = await _context.UsrUserProgramInstances
                .Include(p => p.Program)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.InstanceId == instanceId.Value);
        }
        else
        {
            program = await _context.UsrUserProgramInstances
                .Include(p => p.Program)
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
        }

        if (program == null) 
            throw new KeyNotFoundException("No active program found.");

        // CHEAT ENGINE LOGIC: Add exactly 28 days to simulate completing the current month
        // Example: Day 1 becomes Day 29. Day 29 becomes Day 57 (Month 3).
        program.CurrentDayNo += ProgramConstants.DaysPerCycle;
        
        await _context.SaveChangesAsync();

        // This will automatically route to HandleLevelUpAsync since CurrentDayNo > 28
        return await ProcessProgressionLogicAsync(program);
    }

    // ── Private Helpers ──────────────────────────────────────────

    private async Task<object> ProcessProgressionLogicAsync(UsrUserProgramInstance activeProgram)
    {
        if (activeProgram.CurrentDayNo > ProgramConstants.DaysPerCycle)
        {
            return await HandleLevelUpAsync(activeProgram);
        }

        return new
        {
            message = "Consistency is key! Keep going!",
            currentDay = activeProgram.CurrentDayNo,
            daysRemaining = ProgramConstants.DaysPerCycle - activeProgram.CurrentDayNo + 1,
            instanceId = activeProgram.InstanceId
        };
    }

    private async Task<object> HandleLevelUpAsync(UsrUserProgramInstance activeProgram)
    {
        // 1. Determine Next Level (Beginner -> Intermediate -> Advanced -> Repeat Advanced)
        string currentLevel = (activeProgram.FitnessLevelAtStart ?? "BEGINNER").Trim().ToUpper();

        bool isRehab = ProgramRules.IsRehabTemplate(activeProgram.Program);
        
        string nextLevel; 
        
        if (isRehab)
        {
            nextLevel = ProgramRules.RehabLevel;
        }
        else
        {
            nextLevel = currentLevel switch
            {
                "BEGINNER" => "INTERMEDIATE",
                "INTERMEDIATE" => "ADVANCED",
                _ => "ADVANCED" // Pag Advanced na, stay lang sa Advanced (Repeat Routine)   
            };
        };

        // 2 STRICT Category Match (no fallback)
        var currentCategory = activeProgram.Program?.ProgramCategory;

        // 3. Hanapin ang tamang Template
        var nextTemplate = await _context.WrkProgramTemplates
            .Where(t => t.IsActive 
                && t.FitnessLevel.ToUpper() == nextLevel
                && (currentCategory == null || t.ProgramCategory == currentCategory))
            .OrderByDescending(t => t.ProgramId) 
            .FirstOrDefaultAsync();
            
        if (nextTemplate == null) 
        {
            throw new KeyNotFoundException($"No template found for {nextLevel} level and category '{currentCategory}'. Please seed the database.");
        }        

        // 4. BURAHIN ANG LUMANG DATA (Safety First: Workouts -> Sessions -> Instances)

        // A. Kunin muna ang Session IDs para mabura ang SessionWorkouts (Iwas FK Error)        
        var sessionIdsToDelete = await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId)
            .Select(s => s.SessionId)
            .ToListAsync();

        if (sessionIdsToDelete.Any())
        {
            await _context.UsrUserSessionWorkouts
                .Where(sw => sessionIdsToDelete.Contains(sw.SessionId))
                .ExecuteDeleteAsync();
        }

        // B. Burahin ang Sessions at SessionInstances
        await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId)
            .ExecuteDeleteAsync();

        await _context.UsrUserSessionInstances
            .Where(i => i.InstanceId == activeProgram.InstanceId)
            .ExecuteDeleteAsync();

        await _context.WktWorkoutCalendars
            .Where(w => w.InstanceId == activeProgram.InstanceId)
            .ExecuteDeleteAsync();

        // 5. Update Level sa Profile at Instance (I-assign ang bagong ProgramId)
        string formattedLevel = isRehab
            ?(activeProgram.Program?.FitnessLevel ?? "Rehab Level")
            : nextLevel.Substring(0, 1).ToUpper() + nextLevel.Substring(1).ToLower();

        activeProgram.ProgramId = nextTemplate.ProgramId;
        activeProgram.FitnessLevelAtStart = formattedLevel;

        // CONTINUOUS VS RESET LOGIC
        bool isContinuous = (nextLevel == "ADVANCED" || isRehab);
        
        if (!isContinuous)
        {
            // Beginner/Intermediate: Reset to Day 1 (Start of new month)
            activeProgram.CurrentDayNo = 1;
        }
        else
        {
            // ADVANCED/REHAB PLATEAU LOGIC:
            // Huwag i-reset. Panatilihin ang continuous day count para tumataas ang MonthNo.
            // (e.g., Day 29 stays 29, or Day 57 stays 57. Math formula will compute Month 2 or Month 3).
            // CurrentDayNo is already correctly set by FastForwardToEndAsync (+28 days).
        }

        activeProgram.Status = "ACTIVE";
        activeProgram.CompletedAt = null;
        
        await _context.SaveChangesAsync();

        // 6. Generate New Slots para sa bagong cycle/level
        // PASS THE CURRENT DAY NO AS START DAY FOR CONTINUOUS PROGRESSION
        await GenerateProgramSlotsForNewCycleAsync(
            activeProgram.InstanceId, 
            activeProgram.UserId, 
            activeProgram.ProgramId, 
            formattedLevel,
            activeProgram.CurrentDayNo);

        return new { 
            message = isRehab 
                ? "Rehab cycle completed! 💪 Repeating your rehab routine."
                : isContinuous 
                    ? $"Advanced/Rehab mode: Continuing your lifelong fitness journey at Day {activeProgram.CurrentDayNo}! 🚀"
                    : $"Level Up to {formattedLevel} Success! 🚀 New cycle generated.",
            newLevel = formattedLevel,
            newProgramId = nextTemplate.ProgramId,
            instanceId = activeProgram.InstanceId,
            isContinuous = isContinuous,
            simulatedMonth = ((activeProgram.CurrentDayNo - 1) / ProgramConstants.DaysPerCycle) + 1 // For admin verification
        };
    }

    /// <summary>
    /// Generates new workout slots. Supports continuous progression via startDay parameter.
    /// </summary>
    private async Task GenerateProgramSlotsForNewCycleAsync(int instanceId, int userId, int programId, string fitnessLevel, int startDay = 1)
    {
        // 1. Kunin ang Template Days (Structure ng week: e.g., Day 1 = Push, Day 2 = Pull)
        // Ito ay usually 7 days lang (isang weekly template)

        var templateDays = await _context.WrkProgramTemplateDays
            .AsNoTracking()
            .Where(td => td.ProgramId == programId && td.WeekNo == 1)
            .OrderBy(td => td.DayNo)
            .ToListAsync();

        var sessionsToAdd = new List<UsrUserWorkoutSession>();

        // 1. Create all Main Sessions first (from startDay to startDay + 28)
        for (int day = startDay; day < startDay + ProgramConstants.DaysPerCycle; day++)
        {
            // Gamitin ang module para mag-cycle sa 7-day pattern ng Week 1
            int dayOfWeek = ((day - 1) % ProgramConstants.DaysPerWeek) + 1;
            var workoutForThisDay = templateDays.FirstOrDefault(td => td.DayNo == dayOfWeek);

            if (workoutForThisDay == null || workoutForThisDay.DayType == "REST") continue;

            sessionsToAdd.Add(new UsrUserWorkoutSession
            {
                UserId = userId,
                ProgramInstanceId = instanceId,
                WorkoutDay = day,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow // UtcNow para sa consistency
            });
        }

        _context.UsrUserWorkoutSessions.AddRange(sessionsToAdd);
        await _context.SaveChangesAsync(); // Save once to generate SessionIds

        // 3. Load this program's whole pool ONCE and pick per session in memory.
        var poolRows = await _context.WrkProgramTemplateDaytypeWorkouts
            .AsNoTracking()
            .Include(dw => dw.Workout)
            .Where(dw => dw.ProgramId == programId)
            .ToListAsync();

        var poolByWeekAndDayType = poolRows
            .GroupBy(dw => (dw.WeekNo, dw.DayType))
            .ToDictionary(g => g.Key, g => g.ToList());

        double targetBurn = await _poolSelector.GetTargetBurnAsync(userId);
        var allowedLevels = new[] { fitnessLevel };

        // Fetch exercises and map to SessionWorkouts
        var workoutsToAdd = new List<UsrUserSessionWorkout>();

        foreach (var session in sessionsToAdd)
        {
            // 1. Map session to template day type (day 1- 7 pattern)
            int templateDayNo = ((session.WorkoutDay -1) % ProgramConstants.DaysPerWeek) + 1;  
            var dayDef = templateDays.First(td => td.DayNo == templateDayNo);

            // 2. DYNAMIC WEEK CALCULATION: 
            // Day 1-7 = Week 1 | Day 8-14 = Week 2 | Day 15-21 = Week 3 | Day 22-28 = Week 4
            int currentWeek = ((session.WorkoutDay - 1) / ProgramConstants.DaysPerWeek) + 1;

            poolByWeekAndDayType.TryGetValue((currentWeek, dayDef.DayType), out var dayPool);

            // USE WORKOUT POOL SELECTOR (4-5 cap, calorie stop, deduplication, level filter)
            var picked = WorkoutPoolSelector.Pick(
                dayPool ?? new List<WrkProgramTemplateDaytypeWorkout>(),
                allowedLevels,
                targetBurn);

            workoutsToAdd.AddRange(WorkoutPoolSelector.BuildSessionWorkouts(session.SessionId, picked));
        }

        _context.UsrUserSessionWorkouts.AddRange(workoutsToAdd);
        await _context.SaveChangesAsync(); // Save all workouts at once
    }
}