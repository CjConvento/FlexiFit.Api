using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class ProgramService
{
    private readonly FlexiFitDbContext _context;

    public ProgramService(FlexiFitDbContext context) => _context = context;

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
    public async Task<object> CheckProgressionAsync(int userId)
    {
        var activeProgram = await GetActiveProgramForProgressionAsync(userId);
        return await ProcessProgressionLogicAsync(activeProgram);
    }

    public async Task<object> FastForwardToEndAsync(int userId)
    {
        var activeProgram = await GetActiveProgramForProgressionAsync(userId);
        
        // I-set sa Day 29 para ma-trigger ang level up logic
        activeProgram.CurrentDayNo = 29;
        await _context.SaveChangesAsync();
        
        return await ProcessProgressionLogicAsync(activeProgram);
    }

    // ── Private Helpers ──────────────────────────────────────────
    private async Task<UsrUserProgramInstance> GetActiveProgramForProgressionAsync(int userId)
    {
        var program = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
            
        // Throw KeyNotFoundException para automatic na maging 404 sa Middleware
        if (program == null) 
            throw new KeyNotFoundException("No active program found.");
            
        return program;
    }

    private async Task<object> ProcessProgressionLogicAsync(UsrUserProgramInstance activeProgram)
    {
        if (activeProgram.CurrentDayNo > 28)
        {
            return await HandleLevelUpAsync(activeProgram);
        }

        return new
        {
            message = "Consistency is key! Keep going!",
            currentDay = activeProgram.CurrentDayNo,
            daysRemaining = 28 - activeProgram.CurrentDayNo + 1
        };
    }

    private async Task<object> HandleLevelUpAsync(UsrUserProgramInstance activeProgram)
    {
        // 1. Kunin ang Profile
        var profile = await _context.UsrUserProfileVersions
            .FirstOrDefaultAsync(p => p.UserId == activeProgram.UserId && p.IsCurrent);
            
        if (profile == null) 
            throw new InvalidOperationException("Profile missing.");

        // 2. Determine Next Level
        string currentLevel = (profile.FitnessLevelSelected ?? "BEGINNER").Trim().ToUpper();
        string nextLevel = currentLevel switch
        {
            "BEGINNER" => "INTERMEDIATE",
            "INTERMEDIATE" => "ADVANCED",
            _ => "ADVANCED"
        };

        // 3. Hanapin ang tamang Template
        var nextTemplate = await _context.WrkProgramTemplates
            .Where(t => t.IsActive && t.FitnessLevel.ToUpper() == nextLevel)
            .OrderByDescending(t => t.ProgramId)
            .FirstOrDefaultAsync();
            
        if (nextTemplate == null) 
            throw new KeyNotFoundException($"No template found for {nextLevel} level.");

        // 4. Gamitin ang ExecuteDeleteAsync (Direct SQL DELETE)
        await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId)
            .ExecuteDeleteAsync();
            
        await _context.UsrUserSessionInstances
            .Where(i => i.InstanceId == activeProgram.InstanceId)
            .ExecuteDeleteAsync();

        // 5. Update Level sa Profile at Instance
        string formattedLevel = nextLevel.Substring(0, 1).ToUpper() + nextLevel.Substring(1).ToLower();
        profile.FitnessLevelSelected = formattedLevel;
        activeProgram.ProgramId = nextTemplate.ProgramId;
        activeProgram.FitnessLevelAtStart = formattedLevel;
        activeProgram.CurrentDayNo = 1;
        await _context.SaveChangesAsync();

        // 6. Generate New Slots
        await GenerateProgramSlotsForNewCycleAsync(activeProgram.InstanceId, activeProgram.UserId, activeProgram.ProgramId);

        return new { message = $"Level Up to {formattedLevel} Success! 🚀" };
    }

    private async Task GenerateProgramSlotsForNewCycleAsync(int instanceId, int userId, int programId)
    {
        // I-batch ang pag-add at mag-SaveChanges lang isang beses
        var templateDays = await _context.WrkProgramTemplateDays
            .AsNoTracking()
            .Where(td => td.ProgramId == programId)
            .ToListAsync();

        var sessionsToAdd = new List<UsrUserWorkoutSession>();
        var workoutsToAdd = new List<UsrUserSessionWorkout>();

        // 1. Create all Main Sessions first
        for (int day = 1; day <= 28; day++)
        {
            var workoutForThisDay = templateDays.FirstOrDefault(td => td.DayNo == day);
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

        // 2. Fetch exercises and map to SessionWorkouts
        foreach (var session in sessionsToAdd)
        {
            var dayDef = templateDays.First(td => td.DayNo == session.WorkoutDay);
            
            var exercises = await _context.WrkProgramTemplateDaytypeWorkouts
                .AsNoTracking()
                .Where(w => w.ProgramId == programId && w.DayType == dayDef.DayType)
                .OrderBy(w => w.WorkoutOrder)
                .Take(8)
                .ToListAsync();

            workoutsToAdd.AddRange(exercises.Select(ex => new UsrUserSessionWorkout
            {
                SessionId = session.SessionId,
                WorkoutId = ex.WorkoutId,
                Sets = ex.SetsDefault,
                Reps = ex.RepsDefault,
                LoadKg = 0,
                OrderNo = ex.WorkoutOrder
            }));
        }

        _context.UsrUserSessionWorkouts.AddRange(workoutsToAdd);
        await _context.SaveChangesAsync(); // Save all workouts at once
    }
}