using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using FlexiFit.Api.Mappers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FlexiFit.Api.Extensions;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/workout")]
public class WorkoutController : ControllerBase
{
    private readonly FlexiFitDbContext _context;
    private readonly ILogger<WorkoutController> _logger;
    private readonly ProgramProgressionService _progressionService;
    private readonly WorkoutPlanGenerator _workoutPlanGenerator;
    private readonly ProgramService _programService; 

    public WorkoutController(
        FlexiFitDbContext context, 
        ILogger<WorkoutController> logger,
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

    [HttpGet("today")]
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetTodayWorkout([FromQuery] int programNumber = 1)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        int userInt = userId.Value; 

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            // Gamitin ang Helper para kunin ang lahat ng active programs
            var activePrograms = await _programService.GetActiveProgramsAsync(userInt);
            if (activePrograms.Count == 0)
            {
                return NotFound(new { message = "No active program found. Please start a program first." });
            }

            // 1. Safe clamp para iwas IndexOutOfRangeException
            int targetIndex = Math.Clamp(programNumber - 1, 0, activePrograms.Count - 1);
            var activeProgram = activePrograms[targetIndex];

            // 2. Kunin ang Total Programs at Current Program Number
            int currentProgramNo = activePrograms.IndexOf(activeProgram) + 1;
            int totalPrograms = activePrograms.Count;

            _logger.LogInformation($"User {userId} - Current Day: {activeProgram.CurrentDayNo}, Program: {currentProgramNo}/{totalPrograms}");

            string fitnessLevel = activeProgram.Program?.FitnessLevel ?? "Beginner";
            bool isRehab = activeProgram.Program?.ProgramName?.Contains("Rehab", StringComparison.OrdinalIgnoreCase) == true;

            // 3. Calculate day indices
            int currentDay = activeProgram.CurrentDayNo;
            int templateDayNo = ((currentDay - 1) % 7) + 1;
            int weekNo = ((currentDay - 1) / 7) + 1;
            int monthNo = ((currentDay - 1) / 28) + 1;

            // 4. Get Day Type (project only DayType)
            var dayDef = await _context.WrkProgramTemplateDays
                .Where(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1)
                .Select(d => new { d.DayType })
                .FirstOrDefaultAsync();

            if (dayDef == null)
            {
                _logger.LogWarning($"No day definition found for Program {activeProgram.ProgramId}, Day {templateDayNo}");
                return NotFound(new { message = $"Workout configuration not found for day {currentDay}" });
            }

            bool isRestDay = dayDef.DayType.Contains("REST", StringComparison.OrdinalIgnoreCase);

            // 5. Get or create session for today (project only needed fields)
            var session = await _context.UsrUserWorkoutSessions
                .Where(s => s.UserId == userInt
                         && s.ProgramInstanceId == activeProgram.InstanceId
                         && s.WorkoutDay == currentDay)
                .Select(s => new
                {
                    s.SessionId,
                    s.Status,
                    s.StartedAt,
                    s.CompletedAt,
                    s.CreatedAt
                })
                .FirstOrDefaultAsync();

            // COMMON PROGRAM DTO MAPPER (Para hindi ulit-ulitin ang code sa Rest at Workout day)
            var programDto = WorkoutMapper.BuildProgramDto(activeProgram, currentProgramNo, totalPrograms, monthNo, weekNo, currentDay);

            // -------------------- REST DAY HANDLING (unchanged logic, but efficient) --------------------
            if (isRestDay)
            {
                // Get or create calendar entry for today
                var calendarDay = await _context.NtrMealPlanCalendars
                    .Where(c => c.CycleId == activeProgram.CycleNo && c.DayNo == currentDay)
                    .Select(c => new { c.Status, c.CalendarId })
                    .FirstOrDefaultAsync();

                if (calendarDay == null || calendarDay.Status != "DONE")
                {
                    if (calendarDay == null)
                    {
                        _context.NtrMealPlanCalendars.Add(new NtrMealPlanCalendar
                        {
                            CycleId = activeProgram.CycleNo,
                            PlanDate = todayDateOnly,
                            WeekNo = weekNo,
                            DayNo = currentDay,
                            TemplateId = 1,
                            VariationCode = "A",
                            IsWorkoutDay = false,
                            Status = "DONE",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        var existing = await _context.NtrMealPlanCalendars
                            .FirstAsync(c => c.CalendarId == calendarDay.CalendarId);
                        existing.Status = "DONE";
                        existing.UpdatedAt = DateTime.UtcNow;
                    }

                    // (rest day = auto-complete both, so use the same service):
                    // Mark workout calendar as DONE first, then advance
                    var restWorkoutCal = await _context.WktWorkoutCalendars
                        .FirstOrDefaultAsync(w => w.UserId == userInt && w.PlanDate == todayDateOnly);
                    if (restWorkoutCal == null)
                    {
                        _context.WktWorkoutCalendars.Add(new WktWorkoutCalendar
                        {
                            UserId = userInt,
                            CycleId = activeProgram.CycleNo,
                            PlanDate = todayDateOnly,
                            WeekNo = weekNo,
                            DayNo = currentDay,
                            TemplateId = 1,
                            VariationCode = "A",
                            IsWorkoutDay = false,
                            Status = "DONE",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync();
                    }
                    await _progressionService.TryAdvanceAsync(userInt, todayDateOnly);
                }

                // Return rest day response
                return Ok(new DailyWorkoutPlanDto
                {
                    DayNo = currentDay,
                    DayType = "REST DAY",
                    Status = "REST",
                    Level = activeProgram.Program?.FitnessLevel,
                    FocusArea = "Recovery",
                    Message = "Today is a rest day! Time to recover and recharge. 🧘",
                    CanSkip = false,
                    Program = programDto, // Gamitin ang common mapper
                    Warmups = new List<WorkoutExerciseDto>(),
                    Workouts = new List<WorkoutExerciseDto>(),
                    TotalCalories = 0,
                    TotalDuration = 0
                });
            }

            // -------------------- WORKOUT DAY HANDLING --------------------
            if (session == null)
            {
                _logger.LogInformation($"Creating new workout session for User {userId}, Day {currentDay}");

                var newSession = new UsrUserWorkoutSession
                {
                    UserId = userInt,
                    ProgramInstanceId = activeProgram.InstanceId,
                    WorkoutDay = currentDay,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow,
                    StartedAt = DateTime.UtcNow
                };
                _context.UsrUserWorkoutSessions.Add(newSession);
                await _context.SaveChangesAsync();

                // Use WorkoutPlanGenerator instead of CreateSessionWorkouts
                var generatedWorkouts = await _workoutPlanGenerator.GenerateForSessionAsync(
                    newSession.SessionId,
                    activeProgram.ProgramId,
                    dayDef.DayType,
                    weekNo,
                    fitnessLevel,
                    isRehab);
                
                if (generatedWorkouts.Any())
                {
                    _context.UsrUserSessionWorkouts.AddRange(generatedWorkouts);
                    await _context.SaveChangesAsync();
                }
                
                //  REORDER: Warmups muna, tapos main workouts
                await _workoutPlanGenerator.ReorderWorkoutsAsync(newSession.SessionId);

                // Ensure calendar entry exists
                await EnsureCalendarEntryExists(activeProgram.CycleNo, currentDay, activeProgram.CurrentDayNo, false);

                // No need for EnsureWarmupsExist – already added in CreateSessionWorkouts
                session = new { newSession.SessionId, newSession.Status, StartedAt = newSession.StartedAt, CompletedAt = newSession.CompletedAt, CreatedAt = newSession.CreatedAt };
            }

            bool isCompleted = session.Status?.ToUpper() == "COMPLETED";
            bool isSkipped = session.Status?.ToUpper() == "SKIPPED";
            bool canSkip = !isCompleted && !isSkipped;

            // 6. Get warmups and workouts for this session (use full entity for simplicity)
            var allWorkouts = await _context.UsrUserSessionWorkouts
                .Include(sw => sw.Workout)
                .Where(sw => sw.SessionId == session.SessionId)
                .OrderBy(sw => sw.OrderNo)
                .ToListAsync();

            //  Repair missing warmups for pending sessions (with rehab support)
            if (!isCompleted && !isSkipped)
            {
                var newWarmups = await _workoutPlanGenerator.EnsureWarmupsAsync(
                    session.SessionId,
                    activeProgram.ProgramId,
                    dayDef.DayType,
                    weekNo,
                    fitnessLevel,
                    isRehab);

                if (newWarmups.Any())
                {
                    _context.UsrUserSessionWorkouts.AddRange(newWarmups);
                    await _context.SaveChangesAsync();
                }

                //  REORDER: Warmups muna, tapos main workouts
                await _workoutPlanGenerator.ReorderWorkoutsAsync(session.SessionId);

                // Reload to include the newly added warmups
                allWorkouts = await _context.UsrUserSessionWorkouts
                    .Include(sw => sw.Workout)
                    .Where(sw => sw.SessionId == session.SessionId)
                    .OrderBy(sw => sw.OrderNo)
                    .ToListAsync();
            }

            // Separate warmups from regular workouts
            var warmups = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP")
                .Select(w => WorkoutMapper.ToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            var workouts = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
                .Select(w => WorkoutMapper.ToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            // 7. Build response
            var response = WorkoutMapper.BuildDailyWorkoutPlanDto(
            currentDay, dayDef.DayType, session.Status ?? "PENDING",
            activeProgram.Program?.FitnessLevel,
            WorkoutMapper.GetStatusMessage(session.Status),
            canSkip,
            canSkip ? "Skipping will mark this day as skipped and move you to tomorrow's workout." : null,
            session.SessionId, programDto, warmups, workouts);


            _logger.LogInformation($"Returning workout plan for User {userId}, Day {currentDay}, Status: {response.Status}, Warmups: {warmups.Count}, Workouts: {workouts.Count}");
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting today's workout for user {userId}");
            var inner = ex.InnerException;
            while (inner != null)
            {
                _logger.LogError(inner, "Inner exception: {Message}", inner.Message);
                inner = inner.InnerException;
            }
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("complete")]
    public async Task<ActionResult<WorkoutSessionResultDto>> CompleteSession([FromBody] WorkoutSessionCompleteDto req)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
        bool isSkipped = req.Status.ToUpper() == "SKIPPED";

        _logger.LogInformation($"=== WORKOUT COMPLETE/SKIP REQUEST for User {userId} ===");
        _logger.LogInformation($"Request: SessionId={req.SessionId}, Status={req.Status}, Skipped={isSkipped}");

        try
        {
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
            if (activeProgram == null)
                return BadRequest(new { message = "No active program found." });

            int originalDay = activeProgram.CurrentDayNo;

            // Guard: check if the workout calendar entry for today is already DONE
            var existingCalendar = await _context.WktWorkoutCalendars
                .FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == todayDateOnly);
            if (existingCalendar?.Status == "DONE")
            {
                _logger.LogWarning($"Workout for day {originalDay} is already DONE.");
                return Ok(new WorkoutSessionResultDto
                {
                    Message = "Today's workout has already been completed.",
                    CurrentDay = originalDay,
                    NextDay = activeProgram.CurrentDayNo,
                    IsProgramFinished = activeProgram.Status == "COMPLETED",
                    Status = "COMPLETED",
                    WasSkipped = false
                });
            }

            // Guard: prevent completing a session that is already completed/skipped
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == originalDay);
            if (session != null && (session.Status?.ToUpper() == "COMPLETED" || session.Status?.ToUpper() == "SKIPPED"))
            {
                _logger.LogWarning($"Session for Day {originalDay} already has status {session.Status}.");
                return Ok(new WorkoutSessionResultDto
                {
                    Message = $"This workout has already been {session.Status.ToLower()}.",
                    CurrentDay = session.WorkoutDay,
                    NextDay = activeProgram.CurrentDayNo,
                    IsProgramFinished = activeProgram.Status == "COMPLETED",
                    Status = session.Status,
                    WasSkipped = session.Status?.ToUpper() == "SKIPPED"
                });
            }

            int templateDayNo = ((originalDay - 1) % 7) + 1;
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1);
            bool isRestDay = dayDef?.DayType?.Contains("REST", StringComparison.OrdinalIgnoreCase) ?? false;

            // Create or update workout calendar entry
            var workoutCalendar = await _context.WktWorkoutCalendars
                .FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == todayDateOnly);
            string workoutStatus = isSkipped ? "SKIPPED" : "DONE";

            if (workoutCalendar == null)
            {
                workoutCalendar = new WktWorkoutCalendar
                {
                    UserId = userId.Value,
                    CycleId = activeProgram.CycleNo,
                    PlanDate = todayDateOnly,
                    WeekNo = ((originalDay - 1) / 7) + 1,
                    DayNo = originalDay,
                    TemplateId = 1,
                    VariationCode = "A",
                    IsWorkoutDay = !isRestDay,
                    Status = workoutStatus,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.WktWorkoutCalendars.Add(workoutCalendar);
            }
            else
            {
                if (workoutCalendar.Status == "DONE")
                    return BadRequest(new { message = "Workout already marked as DONE." });
                workoutCalendar.Status = workoutStatus;
                workoutCalendar.UpdatedAt = DateTime.UtcNow;
            }

            // Handle session status
            if (!isRestDay)
            {
                if (session == null && !isSkipped)
                {
                    session = new UsrUserWorkoutSession
                    {
                        UserId = userId.Value,
                        ProgramInstanceId = activeProgram.InstanceId,
                        WorkoutDay = originalDay,
                        Status = "COMPLETED",
                        StartedAt = DateTime.UtcNow.AddMinutes(-(req.TotalMinutes > 0 ? req.TotalMinutes : 30)),
                        CompletedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UsrUserWorkoutSessions.Add(session);
                }
                else if (session != null)
                {
                    session.Status = isSkipped ? "SKIPPED" : "COMPLETED";
                    session.CompletedAt = DateTime.UtcNow;
                    if (!isSkipped && req.TotalMinutes > 0)
                        session.StartedAt = DateTime.UtcNow.AddMinutes(-req.TotalMinutes);
                }
            }

            await _context.SaveChangesAsync();

            // ═══════════════════════════════════════════════════════════════
            // ITEM 1: Upsert ActActivitySummary (Workout Calories)
            // ═══════════════════════════════════════════════════════════════
            if (!isSkipped && req.TotalCalories > 0)
            {
                // Safe cast from double/decimal to int para iwas compilation error
                int caloriesToLog = (int)Math.Round(Convert.ToDouble(req.TotalCalories));
                int minutesToLog = req.TotalMinutes > 0 ? req.TotalMinutes : 30; // Fallback to 30 mins kung 0
                
                await UpsertActivitySummaryAsync(userId.Value, todayDateOnly, caloriesToLog, minutesToLog);
                
                _logger.LogInformation($"[Item 1] Upserted ActActivitySummary: UserId={userId}, Date={todayDateOnly}, Calories={caloriesToLog}, Minutes={minutesToLog}");
            }
            // ═══════════════════════════════════════════════════════════════

            // Try to advance program day (only if both are done)
            await _progressionService.TryAdvanceAsync(userId.Value, todayDateOnly);

            // Reload active program to get updated day number
            activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            bool advanced = activeProgram!.CurrentDayNo > originalDay;
            int nextDay = advanced ? activeProgram.CurrentDayNo : originalDay + 1;

            string message = isSkipped ? $"Day {originalDay} skipped." :
                             isRestDay ? "Rest day completed!" :
                             $"🎉 Great job! Day {originalDay} completed!";
            if (advanced)
                message += $" Moving to Day {nextDay}!";
            else
                message += " Complete nutrition to unlock the next day!";

            var result = new WorkoutSessionResultDto
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

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error completing workout for user {userId}");
            return StatusCode(500, new { message = "An error occurred while completing your workout." });
        }
    }

    [HttpGet("can-skip")]
    public async Task<IActionResult> CanSkipToday()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return Ok(new { canSkip = false, reason = "No active program", currentDay = 0 });

            int templateDayNo = ((activeProgram.CurrentDayNo - 1) % 7) + 1;
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1);   //  ensure we use week 1 pattern);

            bool isRestDay = dayDef?.DayType?.Contains("REST", StringComparison.OrdinalIgnoreCase) ?? false;

            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == activeProgram.CurrentDayNo);

            bool alreadyProcessed = session != null &&
                (session.Status?.ToUpper() == "COMPLETED" || session.Status?.ToUpper() == "SKIPPED");

            return Ok(new
            {
                canSkip = !isRestDay && !alreadyProcessed,
                isRestDay = isRestDay,
                alreadyProcessed = alreadyProcessed,
                currentDay = activeProgram.CurrentDayNo,
                message = isRestDay ? "Rest days cannot be skipped" :
                          alreadyProcessed ? "This day has already been processed" :
                          "You can skip this workout if needed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking skip status for user {userId}");
            return StatusCode(500, new { message = "An error occurred while checking skip status." });
        }
    }

    [HttpGet("history-detail")]
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetWorkoutByDate([FromQuery] int day, [FromQuery] int month, [FromQuery] int programNumber = 1)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        int userInt = userId.Value;
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            // Gamitin ang helper para kunin ang lahat ng active programs (consistent sa GetTodayWorkout)
            var activePrograms = await _programService.GetActiveProgramsAsync(userInt);

            if (activePrograms.Count == 0)
            {
                return NotFound(new { message = "No active program found." });
            }

            // Safe clamp para iwas IndexOutOfRangeException
            int targetIndex = Math.Clamp(programNumber -1, 0, activePrograms.Count -1);
            var activeProgram = activePrograms[targetIndex];

            // Kunin ang Total Programs at Current Program Number gamit ang helpers
            int totalPrograms = activePrograms.Count;
            int currentProgramNo = await _programService.GetProgramNumberAsync(userInt, activeProgram.InstanceId);

            // Validate day (1-28)
            if (day < 1 || day > 28)
                return BadRequest(new { message = "Invalid day number." });

            int templateDayNo = ((day - 1) % 7) + 1;
            int weekNo = ((day - 1) / 7) + 1;
            int monthNo = ((day - 1) / 28) + 1; // month is derived from day (always 1 for a 28‑day cycle)

            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo);

            if (dayDef == null)
                return NotFound(new { message = $"Workout configuration not found for day {day}." });

            bool isRestDay = dayDef.DayType.Contains("REST", StringComparison.OrdinalIgnoreCase);

            // Try to retrieve the session if it exists
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userInt
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == day);

            bool isCompleted = session?.Status?.ToUpper() == "COMPLETED";
            bool isSkipped = session?.Status?.ToUpper() == "SKIPPED";

            // Build the workout list if a session exists and it's a workout day
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
            
            // Common DTO Mapper para consistent ang data structure
            var programDto = WorkoutMapper.BuildProgramDto(activeProgram, currentProgramNo, totalPrograms, monthNo, weekNo, day);

            var response = new DailyWorkoutPlanDto
            {
                DayNo = day,
                DayType = dayDef.DayType,
                Status = status,
                Level = template?.FitnessLevel,
                FocusArea = dayDef.DayType,
                Message = WorkoutMapper.GetStatusMessage(status),
                CanSkip = false, // historical days cannot be skipped
                SkipMessage = null,
                SessionId = session?.SessionId ?? 0,
                Program = programDto,
                Warmups = warmups,
                Workouts = workouts,
                TotalCalories = warmups.Sum(x => x.Calories) + workouts.Sum(x => x.Calories),
                TotalDuration = warmups.Sum(x => x.DurationMinutes) + workouts.Sum(x => x.DurationMinutes)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting workout for user {userInt}, day {day}, month {month}");
            return StatusCode(500, new { message = "An error occurred while fetching workout data." });
        }
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetWorkoutHistory()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return Ok(new List<object>());

            var history = await _context.UsrUserWorkoutSessions
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

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting workout history for user {userId}");
            return StatusCode(500, new { message = "An error occurred while fetching workout history." });
        }
    }

    [HttpGet("session/{sessionId}")]
    public async Task<IActionResult> GetSessionDetails(int sessionId)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId);

            if (session == null)
                return NotFound(new { message = "Session not found" });

            var workouts = await _context.UsrUserSessionWorkouts
                .Include(sw => sw.Workout)
                .Where(sw => sw.SessionId == sessionId)
                .OrderBy(sw => sw.OrderNo)
                .Select(sw => new
                {
                    sw.SessionWorkoutId,
                    sw.WorkoutId,
                    sw.Workout.WorkoutName,
                    sw.Sets,
                    sw.Reps,
                    sw.OrderNo,
                    sw.LoadKg
                })
                .ToListAsync();

            return Ok(new
            {
                sessionId = session.SessionId,
                workoutDay = session.WorkoutDay,
                status = session.Status,
                startedAt = session.StartedAt,
                completedAt = session.CompletedAt,
                workouts = workouts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting session details for session {sessionId}");
            return StatusCode(500, new { message = "An error occurred while fetching session details." });
        }
    }

    #region Private Helper Methods

    private async Task EnsureCalendarEntryExists(int cycleNo, int dayNo, int currentDayNo, bool isRestDay)
    {
        var existingCalendar = await _context.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == cycleNo && c.DayNo == dayNo);

        if (existingCalendar == null)
        {
            var calendarEntry = new NtrMealPlanCalendar
            {
                CycleId = cycleNo,
                PlanDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(dayNo - currentDayNo)),
                WeekNo = ((dayNo - 1) / 7) + 1,
                DayNo = dayNo,
                TemplateId = 1,
                VariationCode = "A",
                IsWorkoutDay = !isRestDay,
                Status = isRestDay ? "DONE" : "PENDING",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.NtrMealPlanCalendars.Add(calendarEntry);
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Created calendar entry for Day {dayNo} with Status: {calendarEntry.Status}");
        }
    }

    /// <summary>
    /// ITEM 1: Upsert logic for ActActivitySummary.
    /// Inserts a new record if none exists for the user+date, 
    /// otherwise updates the existing CaloriesBurned.
    /// </summary>
    private async Task UpsertActivitySummaryAsync(int userId, DateOnly logDate, int caloriesBurned, int totalMinutes)
    {
        // 1. Hanapin kung may existing record na para sa user at date na 'to
        var existingSummary = await _context.ActActivitySummaries
            .FirstOrDefaultAsync(a => a.UserId == userId && a.LogDate == logDate);

        if (existingSummary != null)
        {
            // 2. UPDATE: Idagdag ang bagong calories at minutes (para support multiple sessions per day)
            existingSummary.CaloriesBurned += caloriesBurned;
            existingSummary.TotalMinutes += totalMinutes;
            existingSummary.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // 3. INSERT: Gumawa ng bagong record
            _context.ActActivitySummaries.Add(new ActActivitySummary
            {
                UserId = userId,
                LogDate = logDate,
                CaloriesBurned = caloriesBurned,
                TotalMinutes = totalMinutes,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // I-save agad para ma-read ng NutritionController kapag tinawag
        await _context.SaveChangesAsync();
    }

    private async Task<int> GetTotalDaysForCycle(int cycleId)
    {
        var weeks = await _context.NtrUserCycleTargets
            .Where(t => t.CycleId == cycleId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => (int?)t.WeeksInCycle)   // force nullable
            .FirstOrDefaultAsync();

        return (weeks ?? 4) * 7;
    }   

    #endregion
}