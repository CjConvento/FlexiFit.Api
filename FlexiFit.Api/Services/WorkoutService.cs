using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Mappers;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class WorkoutService
{
    private readonly FlexiFitDbContext _context;
    private readonly ILogger<WorkoutService> _logger;
    private readonly ProgramProgressionService _progressionService;
    private readonly WorkoutPlanGenerator _workoutPlanGenerator;
    private readonly ProgramService _programService;

    public WorkoutService(
        FlexiFitDbContext context,
        ILogger<WorkoutService> logger,
        ProgramProgressionService progressionService,
        WorkoutPlanGenerator workoutPlanGenerator,
        ProgramService programService)
    {
        _context = context;
        _logger = logger;
        _progressionService = progressionService;
        _workoutPlanGenerator = workoutPlanGenerator;
        _programService = programService;
    }

    /// <summary>
    /// Handles the complete logic for getting today's workout plan.
    /// </summary>
    public async Task<DailyWorkoutPlanDto> GetTodayWorkoutAsync(int userId, int programNumber, string baseUrl)
    {
        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        var activePrograms = await _programService.GetActiveProgramsAsync(userId);
        if (activePrograms.Count == 0) throw new KeyNotFoundException("No active program found.");

        int targetIndex = Math.Clamp(programNumber - 1, 0, activePrograms.Count - 1);
        var activeProgram = activePrograms[targetIndex];
        int currentProgramNo = activePrograms.IndexOf(activeProgram) + 1;
        int totalPrograms = activePrograms.Count;
        int currentDay = activeProgram.CurrentDayNo;
        int templateDayNo = ((currentDay - 1) % 7) + 1;
        int weekNo = ((currentDay - 1) / 7) + 1;
        int monthNo = ((currentDay - 1) / 28) + 1;

        var dayDef = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1)
            .Select(d => new { d.DayType })
            .FirstOrDefaultAsync() ?? throw new KeyNotFoundException($"Workout config not found for day {currentDay}");

        bool isRestDay = dayDef.DayType.Contains("REST", StringComparison.OrdinalIgnoreCase);
        var programDto = WorkoutMapper.BuildProgramDto(activeProgram, currentProgramNo, totalPrograms, monthNo, weekNo, currentDay);

        if (isRestDay)
        {
            // REST DAY: Auto-mark WORKOUT only, NOT nutrition
            // Nutrition remains PENDING until user logs meals

            // Handle Rest Day Calendar Logic
            var restWorkoutCal = await GetWorkoutCalendarAsync(userId, todayDateOnly, activeProgram.InstanceId);

            if (restWorkoutCal == null)
            {
                _context.WktWorkoutCalendars.Add(new WktWorkoutCalendar { 
                    UserId = userId, 
                    InstanceId = activeProgram.InstanceId, 
                    DayType = dayDef.DayType, 
                    CycleId = activeProgram.CycleNo, 
                    PlanDate = todayDateOnly, 
                    WeekNo = weekNo, 
                    DayNo = currentDay, 
                    TemplateId = 1, 
                    VariationCode = "A", 
                    IsWorkoutDay = false, 
                    Status = "DONE", // autodone for workout, but nutrition remains PENDING
                    CreatedAt = DateTime.UtcNow, 
                    UpdatedAt = DateTime.UtcNow 
                });
                await _context.SaveChangesAsync();
            }
            // 2. DO NOT auto-mark nutrition calendar
            // Nutrition stays PENDING until user logs meals
            // TryAdvanceAsync will check both workout + nutrition before advancing

            // 3. Try to advance (will only succeed if nutrition is already DONE)
            await _progressionService.TryAdvanceAsync(userId, todayDateOnly, activeProgram.InstanceId);

            return WorkoutMapper.BuildRestDayDto(currentDay, activeProgram.Program?.FitnessLevel, programDto);
        }

        // Handle Workout Day Logic
        var session = await _context.UsrUserWorkoutSessions
            .Where(s => s.UserId == userId && s.ProgramInstanceId == activeProgram.InstanceId && s.WorkoutDay == currentDay)
            .Select(s => new { s.SessionId, s.Status })
            .FirstOrDefaultAsync();

        if (session == null)
        {
            var newSession = new UsrUserWorkoutSession { UserId = userId, ProgramInstanceId = activeProgram.InstanceId, WorkoutDay = currentDay, Status = "PENDING", CreatedAt = DateTime.UtcNow, StartedAt = DateTime.UtcNow };
            _context.UsrUserWorkoutSessions.Add(newSession);
            await _context.SaveChangesAsync();

            var generatedWorkouts = await _workoutPlanGenerator.GenerateForSessionAsync(newSession.SessionId, activeProgram.ProgramId, dayDef.DayType, weekNo, activeProgram.Program?.FitnessLevel ?? "Beginner", activeProgram.Program?.ProgramName?.Contains("Rehab", StringComparison.OrdinalIgnoreCase) == true);
            if (generatedWorkouts.Any())
            {
                _context.UsrUserSessionWorkouts.AddRange(generatedWorkouts);
                await _context.SaveChangesAsync();
            }
            await _workoutPlanGenerator.ReorderWorkoutsAsync(newSession.SessionId);
            await EnsureCalendarEntryExistsAsync(activeProgram.CycleNo, currentDay, activeProgram.CurrentDayNo, false);
            session = new { newSession.SessionId, Status = "PENDING" };
        }

        bool isCompleted = session.Status?.ToUpper() == "COMPLETED";
        bool isSkipped = session.Status?.ToUpper() == "SKIPPED";
        bool canSkip = !isCompleted && !isSkipped;

        var allWorkouts = await _context.UsrUserSessionWorkouts.Include(sw => sw.Workout).Where(sw => sw.SessionId == session.SessionId).OrderBy(sw => sw.OrderNo).ToListAsync();

        if (!isCompleted && !isSkipped)
        {
            var newWarmups = await _workoutPlanGenerator.EnsureWarmupsAsync(session.SessionId, activeProgram.ProgramId, dayDef.DayType, weekNo, activeProgram.Program?.FitnessLevel ?? "Beginner", activeProgram.Program?.ProgramName?.Contains("Rehab", StringComparison.OrdinalIgnoreCase) == true);
            if (newWarmups.Any())
            {
                _context.UsrUserSessionWorkouts.AddRange(newWarmups);
                await _context.SaveChangesAsync();
                await _workoutPlanGenerator.ReorderWorkoutsAsync(session.SessionId);
            }
        }

        var finalWorkouts = await _context.UsrUserSessionWorkouts.Include(sw => sw.Workout).Where(sw => sw.SessionId == session.SessionId).OrderBy(sw => sw.OrderNo).ToListAsync();
        var warmups = finalWorkouts.Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP").Select(w => WorkoutMapper.ToWorkoutExerciseDto(w, baseUrl, isCompleted)).ToList();
        var workouts = finalWorkouts.Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP").Select(w => WorkoutMapper.ToWorkoutExerciseDto(w, baseUrl, isCompleted)).ToList();

        return WorkoutMapper.BuildDailyWorkoutPlanDto(
            currentDay, dayDef.DayType, session.Status ?? "PENDING",
            activeProgram.Program?.FitnessLevel, WorkoutMapper.GetStatusMessage(session.Status),
            canSkip, canSkip ? "Skipping will mark this day as skipped..." : null,
            session.SessionId, programDto, warmups, workouts);
    }

    // ==========================================
    // PRIVATE HELPERS
    // ==========================================

    /// <summary>
    /// The workout-calendar row for ONE program instance on a date.
    /// Never matches another program's row, so programs can't interfere with each other.
    /// </summary>
    private Task<WktWorkoutCalendar?> GetWorkoutCalendarAsync(int userId, DateOnly date, int instanceId)
        => _context.WktWorkoutCalendars
            .FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == date && w.InstanceId == instanceId);

    /// <summary>
    /// Handles the complete logic for getting a historical workout plan by date.
    /// </summary>
    public async Task<DailyWorkoutPlanDto> GetWorkoutByDateAsync(int userId, int day, int month, int programNumber, string baseUrl)
    {
        if (day < 1 || day > 28)
            throw new ArgumentException("Invalid day number.");

        var activePrograms = await _programService.GetActiveProgramsAsync(userId);
        if (activePrograms.Count == 0)
            throw new KeyNotFoundException("No active program found.");

        int targetIndex = Math.Clamp(programNumber - 1, 0, activePrograms.Count - 1);
        var activeProgram = activePrograms[targetIndex];

        int totalPrograms = activePrograms.Count;
        int currentProgramNo = await _programService.GetProgramNumberAsync(userId, activeProgram.InstanceId);

        int templateDayNo = ((day - 1) % 7) + 1;
        int weekNo = ((day - 1) / 7) + 1;
        int monthNo = ((day - 1) / 28) + 1;

        var dayDef = await _context.WrkProgramTemplateDays
            .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo);

        if (dayDef == null)
            throw new KeyNotFoundException($"Workout configuration not found for day {day}.");

        bool isRestDay = dayDef.DayType.Contains("REST", StringComparison.OrdinalIgnoreCase);

        var session = await _context.UsrUserWorkoutSessions
            .FirstOrDefaultAsync(s => s.UserId == userId
                                   && s.ProgramInstanceId == activeProgram.InstanceId
                                   && s.WorkoutDay == day);

        bool isCompleted = session?.Status?.ToUpper() == "COMPLETED";
        bool isSkipped = session?.Status?.ToUpper() == "SKIPPED";

        List<WorkoutExerciseDto> warmups = new();
        List<WorkoutExerciseDto> workouts = new();

        if (!isRestDay && session != null)
        {
            var allWorkouts = await _context.UsrUserSessionWorkouts
                .Include(sw => sw.Workout)
                .Where(sw => sw.SessionId == session.SessionId)
                .OrderBy(sw => sw.OrderNo)
                .ToListAsync();

            warmups = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP")
                .Select(w => WorkoutMapper.ToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            workouts = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
                .Select(w => WorkoutMapper.ToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();
        }

        var template = await _context.WrkProgramTemplates
            .FirstOrDefaultAsync(t => t.ProgramId == activeProgram.ProgramId);

        // Determine the status for display
        string status = "NOT_STARTED";
        if (isRestDay && day <= activeProgram.CurrentDayNo)
            status = "COMPLETED";
        else if (isCompleted)
            status = "COMPLETED";
        else if (isSkipped)
            status = "SKIPPED";
        else if (day < activeProgram.CurrentDayNo)
            status = "SKIPPED";
        else if (day == activeProgram.CurrentDayNo)
            status = "PENDING";
        else
            status = "NOT_STARTED";

        var programDto = WorkoutMapper.BuildProgramDto(activeProgram, currentProgramNo, totalPrograms, monthNo, weekNo, day);

        // ✅ Use the Mapper for a clean, consistent DTO response
        return WorkoutMapper.BuildDailyWorkoutPlanDto(
            day,
            dayDef.DayType,
            status,
            template?.FitnessLevel,
            WorkoutMapper.GetStatusMessage(status),
            false, // Historical days cannot be skipped
            null,  // No skip message for history
            session?.SessionId ?? 0,
            programDto,
            warmups,
            workouts
        );
    }

    /// <summary>
    /// Handles the complete logic for completing/skipping a workout session.
    /// </summary>
    public async Task<WorkoutSessionResultDto> CompleteSessionAsync(int userId, WorkoutSessionCompleteDto req, DateOnly todayDateOnly)
    {
        bool isSkipped = req.Status.ToUpper() == "SKIPPED";
        var activeProgram = await _context.UsrUserProgramInstances.FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE") ?? throw new KeyNotFoundException("No active program found.");
        int originalDay = activeProgram.CurrentDayNo;

        var existingCalendar = await _context.WktWorkoutCalendars.FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == todayDateOnly);
        if (existingCalendar?.Status == "DONE") throw new InvalidOperationException("Today's workout has already been completed.");

        var session = await _context.UsrUserWorkoutSessions.FirstOrDefaultAsync(s => s.UserId == userId && s.ProgramInstanceId == activeProgram.InstanceId && s.WorkoutDay == originalDay);
        if (session != null && (session.Status?.ToUpper() == "COMPLETED" || session.Status?.ToUpper() == "SKIPPED"))
        {
            return new WorkoutSessionResultDto { Message = $"This workout has already been {session.Status.ToLower()}.", CurrentDay = session.WorkoutDay, NextDay = activeProgram.CurrentDayNo, IsProgramFinished = activeProgram.Status == "COMPLETED", Status = session.Status, WasSkipped = session.Status?.ToUpper() == "SKIPPED" };
        }

        int templateDayNo = ((originalDay - 1) % 7) + 1;
        var dayDef = await _context.WrkProgramTemplateDays.FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1);
        bool isRestDay = dayDef?.DayType?.Contains("REST", StringComparison.OrdinalIgnoreCase) ?? false;

        var workoutCalendar = await _context.WktWorkoutCalendars.FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == todayDateOnly);
        string workoutStatus = isSkipped ? "SKIPPED" : "DONE";

        if (workoutCalendar == null)
        {
            _context.WktWorkoutCalendars.Add(new WktWorkoutCalendar { UserId = userId, CycleId = activeProgram.CycleNo, PlanDate = todayDateOnly, WeekNo = ((originalDay - 1) / 7) + 1, DayNo = originalDay, TemplateId = 1, VariationCode = "A", IsWorkoutDay = !isRestDay, Status = workoutStatus, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            if (workoutCalendar.Status == "DONE") throw new InvalidOperationException("Workout already marked as DONE.");
            workoutCalendar.Status = workoutStatus;
            workoutCalendar.UpdatedAt = DateTime.UtcNow;
        }

        if (!isRestDay)
        {
            if (session == null && !isSkipped)
            {
                _context.UsrUserWorkoutSessions.Add(new UsrUserWorkoutSession { UserId = userId, ProgramInstanceId = activeProgram.InstanceId, WorkoutDay = originalDay, Status = "COMPLETED", StartedAt = DateTime.UtcNow.AddMinutes(-(req.TotalMinutes > 0 ? req.TotalMinutes : 30)), CompletedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            }
            else if (session != null)
            {
                session.Status = isSkipped ? "SKIPPED" : "COMPLETED";
                session.CompletedAt = DateTime.UtcNow;
                if (!isSkipped && req.TotalMinutes > 0) session.StartedAt = DateTime.UtcNow.AddMinutes(-req.TotalMinutes);
            }
        }

        await _context.SaveChangesAsync();

        if (!isSkipped && req.TotalCalories > 0)
        {
            int caloriesToLog = (int)Math.Round(Convert.ToDouble(req.TotalCalories));
            int minutesToLog = req.TotalMinutes > 0 ? req.TotalMinutes : 30;
            await UpsertActivitySummaryAsync(userId, todayDateOnly, caloriesToLog, minutesToLog);
        }

        await _progressionService.TryAdvanceAsync(userId, todayDateOnly);
        activeProgram = await _context.UsrUserProgramInstances.FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
        bool advanced = activeProgram!.CurrentDayNo > originalDay;
        int nextDay = advanced ? activeProgram.CurrentDayNo : originalDay + 1;

        string message = isSkipped ? $"Day {originalDay} skipped." : isRestDay ? "Rest day completed!" : $"🎉 Great job! Day {originalDay} completed!";
        if (advanced) message += $" Moving to Day {nextDay}!";
        else message += " Complete nutrition to unlock the next day!";

        return new WorkoutSessionResultDto
        {
            Message = message,
            CurrentDay = originalDay,
            NextDay = nextDay,
            IsProgramFinished = activeProgram.Status == "COMPLETED",
            Status = isSkipped ? "SKIPPED" : (isRestDay ? "REST_COMPLETED" : "COMPLETED"),
            WasSkipped = isSkipped,
            SkipMessage = isSkipped ? "Try not to skip too many days for best results." : null,
            TotalCalories = req.TotalCalories
        };
    }

    // ==========================================
    // WORKOUT SESSION & HISTORY LOGIC
    // ==========================================
    public async Task<object> CanSkipTodayAsync(int userId)
    {
        var activeProgram = await _context.UsrUserProgramInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        if (activeProgram == null)
            return new { canSkip = false, reason = "No active program", currentDay = 0 };

        int templateDayNo = ((activeProgram.CurrentDayNo - 1) % 7) + 1;
        var dayDef = await _context.WrkProgramTemplateDays
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1);

        bool isRestDay = dayDef?.DayType?.Contains("REST", StringComparison.OrdinalIgnoreCase) ?? false;

        var session = await _context.UsrUserWorkoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId
                                   && s.ProgramInstanceId == activeProgram.InstanceId
                                   && s.WorkoutDay == activeProgram.CurrentDayNo);

        bool alreadyProcessed = session != null &&
            (session.Status?.ToUpper() == "COMPLETED" || session.Status?.ToUpper() == "SKIPPED");

        return new
        {
            canSkip = !isRestDay && !alreadyProcessed,
            isRestDay = isRestDay,
            alreadyProcessed = alreadyProcessed,
            currentDay = activeProgram.CurrentDayNo,
            message = isRestDay ? "Rest days cannot be skipped" :
                      alreadyProcessed ? "This day has already been processed" :
                      "You can skip this workout if needed"
        };
    }

    public async Task<object> GetWorkoutHistoryAsync(int userId)
    {
        var activeProgram = await _context.UsrUserProgramInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        if (activeProgram == null)
            return new List<object>();

        return await _context.UsrUserWorkoutSessions
            .AsNoTracking()
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId)
            .OrderBy(s => s.WorkoutDay)
            .Select(s => new
            {
                s.WorkoutDay,
                s.Status,
                s.CompletedAt,
                s.StartedAt
            })
            .ToListAsync();
    }

    public async Task<object> GetSessionDetailsAsync(int sessionId, int userId)
    {
        var session = await _context.UsrUserWorkoutSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);

        if (session == null)
            throw new KeyNotFoundException("Session not found");

        var workouts = await _context.UsrUserSessionWorkouts
            .AsNoTracking()
            .Include(sw => sw.Workout)
            .Where(sw => sw.SessionId == sessionId)
            .OrderBy(sw => sw.OrderNo)
            .Select(sw => new
            {
                sw.SessionWorkoutId,
                sw.WorkoutId,
                WorkoutName = sw.Workout != null ? sw.Workout.WorkoutName : "Unknown",
                sw.Sets,
                sw.Reps,
                sw.OrderNo,
                sw.LoadKg
            })
            .ToListAsync();

        return new
        {
            sessionId = session.SessionId,
            workoutDay = session.WorkoutDay,
            status = session.Status,
            startedAt = session.StartedAt,
            completedAt = session.CompletedAt,
            workouts = workouts
        };
    }

    // ── Private Helpers (Moved from Controller) ──────────────────────────────
    private async Task EnsureCalendarEntryExistsAsync(int cycleNo, int dayNo, int currentDayNo, bool isRestDay)
    {
        var existingCalendar = await _context.NtrMealPlanCalendars.FirstOrDefaultAsync(c => c.CycleId == cycleNo && c.DayNo == dayNo);
        if (existingCalendar == null)
        {
            _context.NtrMealPlanCalendars.Add(new NtrMealPlanCalendar { CycleId = cycleNo, PlanDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(dayNo - currentDayNo)), WeekNo = ((dayNo - 1) / 7) + 1, DayNo = dayNo, TemplateId = 1, VariationCode = "A", IsWorkoutDay = !isRestDay, Status = isRestDay ? "DONE" : "PENDING", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }
    }

    private async Task UpsertActivitySummaryAsync(int userId, DateOnly logDate, int caloriesBurned, int totalMinutes)
    {
        var existingSummary = await _context.ActActivitySummaries.FirstOrDefaultAsync(a => a.UserId == userId && a.LogDate == logDate);
        if (existingSummary != null)
        {
            existingSummary.CaloriesBurned += caloriesBurned;
            existingSummary.TotalMinutes += totalMinutes;
            existingSummary.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.ActActivitySummaries.Add(new ActActivitySummary { UserId = userId, LogDate = logDate, CaloriesBurned = caloriesBurned, TotalMinutes = totalMinutes, UpdatedAt = DateTime.UtcNow });
        }
        await _context.SaveChangesAsync();
    }
}