using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/workout")]
public class WorkoutController : ControllerBase
{
    private readonly FlexiFitDbContext _context;
    private readonly ILogger<WorkoutController> _logger;

    public WorkoutController(FlexiFitDbContext context, ILogger<WorkoutController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("today")]
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetTodayWorkout()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            // 1. Get Active Program Instance (project only needed fields)
            var activeProgram = await _context.UsrUserProgramInstances
                .Where(p => p.UserId == userId && p.Status == "ACTIVE")
                .Select(p => new
                {
                    p.InstanceId,
                    p.ProgramId,
                    p.CurrentDayNo,
                    p.CycleNo,
                    p.FitnessLevelAtStart,
                    p.Status,
                    ProgramName = p.Program != null ? p.Program.ProgramName : "My Program",
                    ProgramDescription = p.Program != null ? p.Program.Description : "",
                    ProgramEnvironment = p.Program != null ? p.Program.Environment : "Any",
                    ProgramLevel = p.Program != null ? p.Program.FitnessLevel : "Beginner"
                })
                .FirstOrDefaultAsync();

            if (activeProgram == null)
                return NotFound(new { message = "No active program found. Please start a program first." });

            _logger.LogInformation($"User {userId} - Current Day: {activeProgram.CurrentDayNo}");

            // 2. Program number (position among active programs)
            int programNumber = await _context.UsrUserProgramInstances
                .CountAsync(p => p.UserId == userId && p.Status == "ACTIVE" && p.InstanceId <= activeProgram.InstanceId);

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
                .Where(s => s.UserId == userId
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

                    // Advance program day (directly, like original logic)
                    await AdvanceProgramDayDirect(activeProgram.InstanceId, activeProgram.CycleNo);
                }

                // Return rest day response
                return Ok(new DailyWorkoutPlanDto
                {
                    DayNo = currentDay,
                    DayType = "REST DAY",
                    Status = "REST",
                    Level = activeProgram.ProgramLevel,
                    FocusArea = "Recovery",
                    Message = "Today is a rest day! Time to recover and recharge. 🧘",
                    CanSkip = false,
                    Program = new WorkoutProgramDto
                    {
                        ProgramId = activeProgram.ProgramId,
                        ProgramName = activeProgram.ProgramName,
                        Description = activeProgram.ProgramDescription,
                        Environment = activeProgram.ProgramEnvironment,
                        Level = activeProgram.ProgramLevel,
                        Status = activeProgram.Status,
                        Month = monthNo,
                        Week = weekNo,
                        Day = currentDay,
                        ProgramNumber = programNumber
                    },
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
                    UserId = userId.Value,
                    ProgramInstanceId = activeProgram.InstanceId,
                    WorkoutDay = currentDay,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow,
                    StartedAt = DateTime.UtcNow
                };
                _context.UsrUserWorkoutSessions.Add(newSession);
                await _context.SaveChangesAsync();

                // Create workout exercises (includes warmups)
                await CreateSessionWorkouts(newSession.SessionId, activeProgram.ProgramId, dayDef.DayType, weekNo);

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

            // Separate warmups from regular workouts
            var warmups = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP")
                .Select(w => MapToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            var workouts = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
                .Select(w => MapToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            // 7. Build response
            var response = new DailyWorkoutPlanDto
            {
                DayNo = currentDay,
                DayType = dayDef.DayType,
                Status = session.Status ?? "PENDING",
                Level = activeProgram.ProgramLevel,
                FocusArea = dayDef.DayType,
                Message = GetStatusMessage(session.Status),
                CanSkip = canSkip,
                SkipMessage = canSkip ? "Skipping will mark this day as skipped and move you to tomorrow's workout." : null,
                SessionId = session.SessionId,
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = activeProgram.ProgramName,
                    Description = activeProgram.ProgramDescription,
                    Environment = activeProgram.ProgramEnvironment,
                    Level = activeProgram.ProgramLevel,
                    Status = activeProgram.Status,
                    Month = monthNo,
                    Week = weekNo,
                    Day = currentDay,
                    ProgramNumber = programNumber
                },
                Warmups = warmups,
                Workouts = workouts,
                TotalCalories = warmups.Sum(x => x.Calories) + workouts.Sum(x => x.Calories),
                TotalDuration = warmups.Sum(x => x.DurationMinutes) + workouts.Sum(x => x.DurationMinutes)
            };

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
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
        bool isSkipped = req.Status.ToUpper() == "SKIPPED";

        _logger.LogInformation($"=== WORKOUT COMPLETE/SKIP REQUEST for User {userId} ===");
        _logger.LogInformation($"Request: SessionId={req.SessionId}, Status={req.Status}, Skipped={isSkipped}");

        try
        {
            // 1. Get Active Program
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return BadRequest(new { message = "No active program found." });

            _logger.LogInformation($"Active Program - Current Day: {activeProgram.CurrentDayNo}, Cycle: {activeProgram.CycleNo}");

            // ✅ Store the original day before any changes
            int originalDay = activeProgram.CurrentDayNo;

            // 2. Get Today's Session
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == originalDay); // changed

            // Guard: Prevent processing if session already completed or skipped
            if (session != null && (session.Status?.ToUpper() == "COMPLETED" || session.Status?.ToUpper() == "SKIPPED"))
            {
                _logger.LogWarning($"Session for Day {originalDay} already has status {session.Status}. Ignoring duplicate request."); // changed
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

            // 3. Check if today is a REST day
            int templateDayNo = ((originalDay - 1) % 7) + 1; // changed
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1);

            bool isRestDay = dayDef?.DayType?.Contains("REST", StringComparison.OrdinalIgnoreCase) ?? false;

            // 4. Update or Create Workout Calendar Entry
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
                    WeekNo = ((originalDay - 1) / 7) + 1, // changed
                    DayNo = originalDay,                  // changed
                    TemplateId = 1,
                    VariationCode = "A",
                    IsWorkoutDay = !isRestDay,
                    Status = workoutStatus,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.WktWorkoutCalendars.Add(workoutCalendar);
                _logger.LogInformation($"Created workout calendar entry for Day {originalDay} with Status: {workoutStatus}"); // changed
            }
            else
            {
                workoutCalendar.Status = workoutStatus;
                workoutCalendar.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation($"Updated workout calendar entry for Day {originalDay} to Status: {workoutStatus}"); // changed
            }

            // 5. Handle Workout Session
            if (!isRestDay)
            {
                if (session == null && !isSkipped)
                {
                    // Create session for completion (if it doesn't exist)
                    session = new UsrUserWorkoutSession
                    {
                        UserId = userId.Value,
                        ProgramInstanceId = activeProgram.InstanceId,
                        WorkoutDay = originalDay, // changed
                        Status = "COMPLETED",
                        StartedAt = DateTime.UtcNow.AddMinutes(-(req.TotalMinutes > 0 ? req.TotalMinutes : 30)),
                        CompletedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UsrUserWorkoutSessions.Add(session);
                    _logger.LogInformation($"Created new workout session for Day {originalDay} with Status: COMPLETED"); // changed
                }
                else if (session != null)
                {
                    // Update existing session (guard already passed, so it's not final)
                    session.Status = isSkipped ? "SKIPPED" : "COMPLETED";
                    session.CompletedAt = DateTime.UtcNow;

                    if (!isSkipped && req.TotalMinutes > 0)
                    {
                        session.StartedAt = DateTime.UtcNow.AddMinutes(-req.TotalMinutes);
                    }

                    _logger.LogInformation($"Updated workout session for Day {originalDay} to Status: {session.Status}"); // changed
                }
            }

            // 6. Save all changes before advancement
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Saved changes for Day {originalDay}"); // changed

            // 7. Try to advance program day (if both nutrition and workout are done)
            await TryAdvanceProgramDay(userId.Value, todayDateOnly);

            // 8. Reload active program to get updated day number
            activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            // ✅ Determine if the day advanced and compute next day
            bool advanced = activeProgram.CurrentDayNo > originalDay;
            int nextDay = advanced ? activeProgram.CurrentDayNo : originalDay + 1;

            string message;
            string skipMessage = null;

            if (isSkipped)
            {
                message = $"Day {originalDay} skipped.";
                if (advanced)
                    message += $" Moving to Day {nextDay}. Remember to stay consistent! 💪";
                else
                    message += " Complete nutrition to advance.";
                skipMessage = "Try not to skip too many days for best results.";
            }
            else if (isRestDay)
            {
                message = $"Rest day completed!";
                if (advanced)
                    message += $" Moving to Day {nextDay}. Enjoy your recovery! 🧘";
                else
                    message += " Complete nutrition to advance.";
            }
            else
            {
                message = $"🎉 Great job! Day {originalDay} completed!";
                if (advanced)
                    message += $" Ready for Day {nextDay}! 💪";
                else
                    message += " Complete nutrition to unlock the next day!";
            }

            var result = new WorkoutSessionResultDto
            {
                Message = message,
                CurrentDay = originalDay,    // ✅ use originalDay
                NextDay = nextDay,
                IsProgramFinished = activeProgram.Status == "COMPLETED",
                Status = isSkipped ? "SKIPPED" : (isRestDay ? "REST_COMPLETED" : "COMPLETED"),
                WasSkipped = isSkipped,
                SkipMessage = skipMessage,
                TotalCalories = req.TotalCalories
            };

            _logger.LogInformation($"=== WORKOUT COMPLETE RESPONSE: {result.Message} ===");
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
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return Ok(new { canSkip = false, reason = "No active program", currentDay = 0 });

            int templateDayNo = ((activeProgram.CurrentDayNo - 1) % 7) + 1;
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1);   // ✅ ensure we use week 1 pattern);

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
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetWorkoutByDate([FromQuery] int day, [FromQuery] int month)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return NotFound(new { message = "No active program found." });

            // Get all active program instances for this user, ordered by ProgramId (or CreatedAt)
            var allActivePrograms = await _context.UsrUserProgramInstances
                .Where(p => p.UserId == userId && p.Status == "ACTIVE")
                .OrderBy(p => p.ProgramId)   // or p.CreatedAt
                .ToListAsync();

            int programNumber = allActivePrograms.FindIndex(p => p.InstanceId == activeProgram.InstanceId) + 1;

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
                .FirstOrDefaultAsync(s => s.UserId == userId
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
                    .Select(w => MapToWorkoutExerciseDto(w, baseUrl, isCompleted))
                    .ToList();

                workouts = allWorkouts
                    .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
                    .Select(w => MapToWorkoutExerciseDto(w, baseUrl, isCompleted))
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

            var response = new DailyWorkoutPlanDto
            {
                DayNo = day,
                DayType = dayDef.DayType,
                Status = status,
                Level = template?.FitnessLevel,
                FocusArea = dayDef.DayType,
                Message = GetStatusMessage(status),
                CanSkip = false, // historical days cannot be skipped
                SkipMessage = null,
                SessionId = session?.SessionId ?? 0,
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = template?.ProgramName ?? "My Program",
                    Description = template?.Description ?? "",
                    Environment = template?.Environment ?? "Any",
                    Level = template?.FitnessLevel ?? "Beginner",
                    Status = activeProgram.Status,
                    Month = monthNo,
                    Week = weekNo,
                    Day = day,
                    ProgramNumber = programNumber   // ✅ ADD THIS
                },
                Warmups = warmups,
                Workouts = workouts,
                TotalCalories = warmups.Sum(x => x.Calories) + workouts.Sum(x => x.Calories),
                TotalDuration = warmups.Sum(x => x.DurationMinutes) + workouts.Sum(x => x.DurationMinutes)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting workout for user {userId}, day {day}, month {month}");
            return StatusCode(500, new { message = "An error occurred while fetching workout data." });
        }
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetWorkoutHistory()
    {
        var userId = GetUserId();
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
        var userId = GetUserId();
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

    private string GetVariationCode(int weekNo)
    {
        int index = (weekNo - 1) % 4;
        return ((char)('A' + index)).ToString();
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

    private async Task AdvanceProgramDayDirect(int instanceId, int cycleNo)
    {
        var program = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.InstanceId == instanceId);
        if (program == null) return;

        int totalDays = await GetTotalDaysForCycle(cycleNo);
        program.CurrentDayNo++;
        if (program.CurrentDayNo > totalDays)
        {
            program.Status = "COMPLETED";
            program.CompletedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
    }

    private async Task TryAdvanceProgramDay(int userId, DateOnly date)
    {
        _logger.LogInformation($"TryAdvanceProgramDay: userId={userId}, date={date}");

        var dailyLog = await _context.NtrDailyLogs
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == date);
        if (dailyLog == null)
        {
            _logger.LogWarning($"No daily log found for user {userId} on {date}");
            return;
        }

        int cycleId = dailyLog.CycleId;
        _logger.LogInformation($"CycleId: {cycleId}");

        var nutritionCalendar = await _context.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == cycleId && c.PlanDate == date);
        if (nutritionCalendar == null)
        {
            _logger.LogWarning($"No nutrition calendar entry found for cycle {cycleId} on {date}");
            return;
        }
        _logger.LogInformation($"Nutrition status: {nutritionCalendar.Status}");

        var workoutCalendar = await _context.WktWorkoutCalendars
            .FirstOrDefaultAsync(w => w.UserId == userId && w.PlanDate == date);
        if (workoutCalendar == null)
        {
            _logger.LogWarning($"No workout calendar entry found for user {userId} on {date}");
            return;
        }
        _logger.LogInformation($"Workout status: {workoutCalendar.Status}");

        if (nutritionCalendar.Status != "DONE" || workoutCalendar.Status != "DONE")
        {
            _logger.LogInformation($"Both not done. Nutrition={nutritionCalendar.Status}, Workout={workoutCalendar.Status}");
            return;
        }

        var activeProgram = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
        if (activeProgram == null)
        {
            _logger.LogWarning("No active program found");
            return;
        }

        var cycleTarget = await _context.NtrUserCycleTargets
            .Where(t => t.UserId == userId && t.CycleId == activeProgram.CycleNo)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        int totalDays = (cycleTarget?.WeeksInCycle ?? 4) * 7;

        _logger.LogInformation($"Current day before advance: {activeProgram.CurrentDayNo}");
        activeProgram.CurrentDayNo++;
        if (activeProgram.CurrentDayNo > totalDays)
        {
            activeProgram.Status = "COMPLETED";
            activeProgram.CompletedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();



        // ========== POPULATE DAILY_PROGRESS_LOG ==========
        // The day that was just completed is the day BEFORE advancement
        int completedDayNo = activeProgram.CurrentDayNo - 1;

        // Get the workout session for the completed day
        var workoutSession = await _context.UsrUserWorkoutSessions
            .Include(s => s.UsrUserSessionWorkouts)
                .ThenInclude(sw => sw.Workout)
            .FirstOrDefaultAsync(s => s.UserId == userId
                                   && s.WorkoutDay == completedDayNo
                                   && s.ProgramInstanceId == activeProgram.InstanceId);

        // Calculate total calories burned from all exercises in the session
        int caloriesBurned = workoutSession?.UsrUserSessionWorkouts
            .Sum(sw => sw.Workout?.CaloriesBurned ?? 0) ?? 0;

        // Get water intake for the completed day
        var waterLog = await _context.NtrWaterLogs
            .Where(w => w.UserId == userId && w.LogDate == date)
            .SumAsync(w => w.WaterMl);

        // Create and insert the progress log record
        var progressLog = new DailyProgressLog
        {
            UserId = userId,
            InstanceId = activeProgram.InstanceId,
            MonthNo = ((completedDayNo - 1) / 28) + 1,
            WeekNo = ((completedDayNo - 1) / 7) + 1,
            DayNo = completedDayNo,
            CaloriesBurned = caloriesBurned,
            CaloriesIntake = dailyLog.CaloriesConsumed,
            WaterMl = waterLog,
            MealPlanCompleted = true,
            FitnessLevelSnapshot = activeProgram.FitnessLevelAtStart,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.DailyProgressLogs.Add(progressLog);
        await _context.SaveChangesAsync();
        // ========== END POPULATE DAILY_PROGRESS_LOG ==========    



        _logger.LogInformation($"Advanced program day to {activeProgram.CurrentDayNo} for user {userId}");
    }

    private async Task EnsureWarmupsExist(int sessionId)
    {
        // Check if session already has warmups
        var hasWarmups = await _context.UsrUserSessionWorkouts
            .Include(sw => sw.Workout)
            .AnyAsync(sw => sw.SessionId == sessionId && sw.Workout.Category != null && sw.Workout.Category.ToUpper() == "WARMUP");

        if (hasWarmups)
        {
            _logger.LogInformation($"Session {sessionId} already has warmups");
            return;
        }

        _logger.LogInformation($"Adding warmups to existing session {sessionId}");

        // Get current max order
        var maxOrder = await _context.UsrUserSessionWorkouts
            .Where(sw => sw.SessionId == sessionId)
            .MaxAsync(sw => (int?)sw.OrderNo) ?? 0;

        // Get 2 random warmups
        var warmups = await _context.WrkWorkouts
            .Where(w => w.Category != null && w.Category.ToUpper() == "WARMUP" && w.IsActive == true)
            .OrderBy(w => Guid.NewGuid())
            .Take(2)
            .ToListAsync();

        if (!warmups.Any())
        {
            _logger.LogWarning("No warmups found in database!");
            return;
        }

        var newWorkouts = new List<UsrUserSessionWorkout>();
        int order = maxOrder + 1;

        foreach (var warmup in warmups)
        {
            newWorkouts.Add(new UsrUserSessionWorkout
            {
                SessionId = sessionId,
                WorkoutId = warmup.WorkoutId,
                Sets = 2,
                Reps = 12,
                OrderNo = order++,
                LoadKg = 0
            });
        }

        _context.UsrUserSessionWorkouts.AddRange(newWorkouts);
        await _context.SaveChangesAsync();

        // Reorder all workouts to put warmups first
        await ReorderWorkouts(sessionId);

        _logger.LogInformation($"Added {newWorkouts.Count} warmups to session {sessionId}");
    }

    private async Task ReorderWorkouts(int sessionId)
    {
        var allWorkouts = await _context.UsrUserSessionWorkouts
            .Include(sw => sw.Workout)
            .Where(sw => sw.SessionId == sessionId)
            .ToListAsync();

        // Separate warmups from main workouts
        var warmups = allWorkouts
            .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP")
            .OrderBy(w => w.OrderNo)
            .ToList();

        var mainWorkouts = allWorkouts
            .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
            .OrderBy(w => w.OrderNo)
            .ToList();

        int order = 1;

        // First assign orders to warmups
        foreach (var warmup in warmups)
        {
            warmup.OrderNo = order++;
        }

        // Then assign orders to main workouts
        foreach (var workout in mainWorkouts)
        {
            workout.OrderNo = order++;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation($"Reordered {allWorkouts.Count} workouts for session {sessionId}");
    }

    private async Task CreateSessionWorkouts(int sessionId, int programId, string dayType, int weekNo)
    {
        _logger.LogInformation($"Creating session workouts for Session {sessionId}, Program {programId}, DayType {dayType}, Week {weekNo}");

        // Check if workouts already exist
        var existingWorkouts = await _context.UsrUserSessionWorkouts
            .AnyAsync(sw => sw.SessionId == sessionId);
        if (existingWorkouts)
        {
            _logger.LogWarning($"Session {sessionId} already has workouts. Skipping creation.");
            return;
        }

        var sessionWorkouts = new List<UsrUserSessionWorkout>();
        int order = 1;

        // 1. Add 2 random warmups (efficient random)
        var warmups = await _context.WrkWorkouts
            .Where(w => w.Category == "WARMUP" && w.IsActive == true)
            .OrderBy(x => EF.Functions.Random()) // SQL Server NEWID()
            .Take(2)
            .ToListAsync();

        foreach (var warmup in warmups)
        {
            sessionWorkouts.Add(new UsrUserSessionWorkout
            {
                SessionId = sessionId,
                WorkoutId = warmup.WorkoutId,
                Sets = 2,
                Reps = 12,
                OrderNo = order++,
                LoadKg = 0
            });
        }

        // 2. Add main workouts from template
        var templateWorkouts = await _context.WrkProgramTemplateDaytypeWorkouts
            .Where(tw => tw.ProgramId == programId && tw.WeekNo == weekNo && tw.DayType == dayType)
            .OrderBy(tw => tw.WorkoutOrder)
            .Take(8)
            .Select(tw => new { tw.WorkoutId, tw.SetsDefault, tw.RepsDefault })
            .ToListAsync();

        if (!templateWorkouts.Any())
        {
            // Fallback to week 1
            templateWorkouts = await _context.WrkProgramTemplateDaytypeWorkouts
                .Where(tw => tw.ProgramId == programId && tw.WeekNo == 1 && tw.DayType == dayType)
                .OrderBy(tw => tw.WorkoutOrder)
                .Take(8)
                .Select(tw => new { tw.WorkoutId, tw.SetsDefault, tw.RepsDefault })
                .ToListAsync();
        }

        if (!templateWorkouts.Any())
        {
            // Fallback to random workouts (excluding warmups and cardio)
            var fallbackWorkouts = await _context.WrkWorkouts
                .Where(w => w.IsActive == true
                         && w.Category != null
                         && w.Category.ToUpper() != "WARMUP"
                         && w.Category.ToUpper() != "CARDIO")
                .OrderBy(x => EF.Functions.Random())
                .Take(8)
                .ToListAsync();

            foreach (var w in fallbackWorkouts)
            {
                sessionWorkouts.Add(new UsrUserSessionWorkout
                {
                    SessionId = sessionId,
                    WorkoutId = w.WorkoutId,
                    Sets = 3,
                    Reps = 12,
                    OrderNo = order++,
                    LoadKg = 0
                });
            }
        }
        else
        {
            foreach (var tw in templateWorkouts)
            {
                sessionWorkouts.Add(new UsrUserSessionWorkout
                {
                    SessionId = sessionId,
                    WorkoutId = tw.WorkoutId,
                    Sets = tw.SetsDefault,
                    Reps = tw.RepsDefault,
                    OrderNo = order++,
                    LoadKg = 0
                });
            }
        }

        // Ensure no duplicate order numbers
        var duplicateOrders = sessionWorkouts
            .GroupBy(w => w.OrderNo)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateOrders.Any())
        {
            for (int i = 0; i < sessionWorkouts.Count; i++)
                sessionWorkouts[i].OrderNo = i + 1;
        }

        _context.UsrUserSessionWorkouts.AddRange(sessionWorkouts);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"Created {sessionWorkouts.Count} workout exercises for Session {sessionId} (Warmups: {warmups.Count}, Main: {sessionWorkouts.Count - warmups.Count})");
    }

    private WorkoutExerciseDto MapToWorkoutExerciseDto(UsrUserSessionWorkout sw, string baseUrl, bool isCompleted)
    {
        // Handle null Workout gracefully
        if (sw.Workout == null)
        {
            _logger.LogWarning($"Workout is null for SessionWorkoutId {sw.SessionWorkoutId}");
            return new WorkoutExerciseDto
            {
                Id = sw.WorkoutId,
                Name = "Unknown Exercise",
                ImageFileName = $"{baseUrl}/images/workouts/default.png",
                MuscleGroup = "Unknown",
                Sets = sw.Sets,
                Reps = sw.Reps,
                DurationMinutes = 10,
                RestSeconds = 60,
                Calories = 50,
                Description = "Exercise details not available",
                VideoUrl = null,
                Order = sw.OrderNo,
                LoadKg = sw.LoadKg,
                IsCompleted = isCompleted
            };
        }

        // Determine image URL
        string imageUrl;
        string category = sw.Workout.Category?.ToUpper();

        if (category == "WARMUP")
        {
            // Always use default image for warmups
            imageUrl = $"{baseUrl}/images/workouts/default.png";
        }
        else if (!string.IsNullOrEmpty(sw.Workout.ImgFilename))
        {
            // Use specific image if available
            imageUrl = $"{baseUrl}/images/workouts/{GetCategoryFolder(sw.Workout.Category)}/{sw.Workout.ImgFilename}";
        }
        else
        {
            // Fallback to default
            imageUrl = $"{baseUrl}/images/workouts/default.png";
        }

        return new WorkoutExerciseDto
        {
            Id = sw.WorkoutId,
            Name = sw.Workout.WorkoutName ?? "Unknown Exercise",
            ImageFileName = imageUrl,
            MuscleGroup = sw.Workout.MuscleGroup ?? "Full Body",
            Sets = sw.Sets,
            Reps = sw.Reps,
            DurationMinutes = sw.Workout.Duration ?? 10,
            RestSeconds = 60,
            Calories = (int)(sw.Workout.CaloriesBurned ?? 50),
            Description = sw.Workout.Notes ?? "Perform with proper form",
            VideoUrl = sw.Workout.VideoUrl,
            Order = sw.OrderNo,
            LoadKg = sw.LoadKg,
            IsCompleted = isCompleted
        };
    }

    private static string GetCategoryFolder(string? category)
    {
        if (string.IsNullOrEmpty(category))
            return "general";

        return category.ToLower() switch
        {
            "cardio" => "cardio",
            "strength" => "strength",
            "hiit" => "hiit",
            "yoga" => "yoga",
            "warmup" => "warmups",
            "push" => "push",
            "pull" => "pull",
            "legs" => "legs",
            "core" => "core",
            _ => "general"
        };
    }

    private static string GetStatusMessage(string? status)
    {
        return status?.ToUpper() switch
        {
            "COMPLETED" => "🎉 Great job! You've completed today's workout!",
            "SKIPPED" => "This day was skipped. Ready for tomorrow? 💪",
            "PENDING" => "Ready to crush it today! Let's go! 💪",
            _ => "Let's get moving! 🏃"
        };
    }

    private int? GetUserId()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                User.FindFirst("user_id")?.Value;

        if (int.TryParse(id, out var userId))
            return userId;

        return null;
    }

    #endregion
}