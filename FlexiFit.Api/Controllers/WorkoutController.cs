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

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            // 1. Get Active Program Instance
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return NotFound(new { message = "No active program found. Please start a program first." });

            _logger.LogInformation($"User {userId} - Current Day: {activeProgram.CurrentDayNo}");

            // 2. Get Program Template
            var template = await _context.WrkProgramTemplates
                .FirstOrDefaultAsync(t => t.ProgramId == activeProgram.ProgramId);

            // 3. Calculate template day pattern (1-7 pattern for 28-day cycle)
            int currentDay = activeProgram.CurrentDayNo;
            int templateDayNo = ((currentDay - 1) % 7) + 1;
            int weekNo = ((currentDay - 1) / 7) + 1;
            int monthNo = ((currentDay - 1) / 28) + 1;

            // 4. Get Day Type from template (Week 1 pattern)
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo);

            if (dayDef == null)
            {
                _logger.LogWarning($"No day definition found for Program {activeProgram.ProgramId}, Day {templateDayNo}");
                return NotFound(new { message = $"Workout configuration not found for day {currentDay}" });
            }

            bool isRestDay = dayDef.DayType.Contains("REST", StringComparison.OrdinalIgnoreCase);

            // 5. Get or Create Session for Today
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == currentDay);

            // For REST days
            if (isRestDay)
            {
                _logger.LogInformation($"Day {currentDay} is a REST day");

                // Ensure calendar entry exists
                await EnsureCalendarEntryExists(activeProgram, currentDay, true);

                return Ok(new DailyWorkoutPlanDto
                {
                    DayNo = currentDay,
                    DayType = "REST DAY",
                    Status = "REST",
                    Level = template?.FitnessLevel,
                    FocusArea = "Recovery",
                    Message = "Today is a rest day! Time to recover and recharge. 🧘",
                    CanSkip = false,
                    SkipMessage = "Rest days are essential for recovery and cannot be skipped.",
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
                        Day = currentDay
                    },
                    Warmups = new List<WorkoutExerciseDto>(),
                    Workouts = new List<WorkoutExerciseDto>(),
                    TotalCalories = 0,
                    TotalDuration = 0
                });
            }

            // For workout days - create session if it doesn't exist
            if (session == null)
            {
                _logger.LogInformation($"Creating new workout session for User {userId}, Day {currentDay}");

                session = new UsrUserWorkoutSession
                {
                    UserId = userId.Value,
                    ProgramInstanceId = activeProgram.InstanceId,
                    WorkoutDay = currentDay,
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow
                };
                _context.UsrUserWorkoutSessions.Add(session);
                await _context.SaveChangesAsync();

                // Create workout exercises for this session (includes warmups)
                await CreateSessionWorkouts(session.SessionId, activeProgram.ProgramId, dayDef.DayType);

                // Ensure calendar entry exists
                await EnsureCalendarEntryExists(activeProgram, currentDay, false);

                // ✅ ADD THIS LINE - Ensure existing session has warmups
                await EnsureWarmupsExist(session.SessionId);
            }

            bool isCompleted = session.Status?.ToUpper() == "COMPLETED";
            bool isSkipped = session.Status?.ToUpper() == "SKIPPED";
            bool canSkip = !isCompleted && !isSkipped;

            // 6. Get Warmups and Workouts - SAFE with null checking
            var allWorkouts = await _context.UsrUserSessionWorkouts
                .Include(sw => sw.Workout)
                .Where(sw => sw.SessionId == session.SessionId)
                .OrderBy(sw => sw.OrderNo)
                .ToListAsync();

            // Separate warmups from regular workouts with null safety
            var warmups = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP")
                .Select(w => MapToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            var workouts = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
                .Select(w => MapToWorkoutExerciseDto(w, baseUrl, isCompleted))
                .ToList();

            // 7. Build Response
            var response = new DailyWorkoutPlanDto
            {
                DayNo = currentDay,
                DayType = dayDef.DayType,
                Status = session.Status ?? "PENDING",
                Level = template?.FitnessLevel,
                FocusArea = dayDef.DayType,
                Message = GetStatusMessage(session.Status),
                CanSkip = canSkip,
                SkipMessage = canSkip ? "Skipping will mark this day as skipped and move you to tomorrow's workout." : null,
                SessionId = session.SessionId,
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
                    Day = currentDay
                },
                Warmups = warmups,
                Workouts = workouts,
                TotalCalories = warmups.Sum(x => x.Calories) + workouts.Sum(x => x.Calories),
                TotalDuration = warmups.Sum(x => x.DurationMinutes) + workouts.Sum(x => x.DurationMinutes)
            };

            _logger.LogInformation($"Returning workout plan for User {userId}, Day {currentDay}, Status: {response.Status}, Warmups: {warmups.Count}, Workouts: {workouts.Count}");
            return Ok(response);

            _logger.LogInformation($"Returning workout plan for User {userId}, Day {currentDay}, Status: {response.Status}");
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting today's workout for user {userId}");
            return StatusCode(500, new { message = "An error occurred while fetching your workout plan." });
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

            // 2. Get Today's Session
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == activeProgram.CurrentDayNo);

            // 3. Check if today is a REST day
            int templateDayNo = ((activeProgram.CurrentDayNo - 1) % 7) + 1;
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo);

            bool isRestDay = dayDef?.DayType?.Contains("REST", StringComparison.OrdinalIgnoreCase) ?? false;

            // 4. CRITICAL: Update or Create Calendar Entry
            var calendarDay = await _context.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == activeProgram.CycleNo
                                       && c.DayNo == activeProgram.CurrentDayNo);

            string calendarStatus = isSkipped ? "SKIPPED" : "DONE";

            if (calendarDay == null)
            {
                calendarDay = new NtrMealPlanCalendar
                {
                    CycleId = activeProgram.CycleNo,
                    PlanDate = todayDateOnly,
                    WeekNo = ((activeProgram.CurrentDayNo - 1) / 7) + 1,
                    DayNo = activeProgram.CurrentDayNo,
                    TemplateId = 1,
                    VariationCode = "A",
                    IsWorkoutDay = !isRestDay,
                    Status = calendarStatus,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.NtrMealPlanCalendars.Add(calendarDay);
                _logger.LogInformation($"Created calendar entry for Day {activeProgram.CurrentDayNo} with Status: {calendarStatus}");
            }
            else
            {
                calendarDay.Status = calendarStatus;
                calendarDay.UpdatedAt = DateTime.UtcNow;
                _logger.LogInformation($"Updated calendar entry for Day {activeProgram.CurrentDayNo} to Status: {calendarStatus}");
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
                        WorkoutDay = activeProgram.CurrentDayNo,
                        Status = "COMPLETED",
                        StartedAt = DateTime.UtcNow.AddMinutes(-(req.TotalMinutes > 0 ? req.TotalMinutes : 30)),
                        CompletedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UsrUserWorkoutSessions.Add(session);
                    _logger.LogInformation($"Created new workout session for Day {activeProgram.CurrentDayNo} with Status: COMPLETED");
                }
                else if (session != null)
                {
                    // Update existing session
                    session.Status = isSkipped ? "SKIPPED" : "COMPLETED";
                    session.CompletedAt = DateTime.UtcNow;

                    if (!isSkipped && req.TotalMinutes > 0)
                    {
                        session.StartedAt = DateTime.UtcNow.AddMinutes(-req.TotalMinutes);
                    }

                    _logger.LogInformation($"Updated workout session for Day {activeProgram.CurrentDayNo} to Status: {session.Status}");
                }
            }

            // 6. Update Activity Summary (only for completed workouts)
            if (!isSkipped && !isRestDay && req.TotalCalories > 0)
            {
                var existingActivity = await _context.ActActivitySummaries
                    .FirstOrDefaultAsync(a => a.UserId == userId && a.LogDate == todayDateOnly);

                if (existingActivity != null)
                {
                    existingActivity.CaloriesBurned += req.TotalCalories;
                    existingActivity.TotalMinutes += req.TotalMinutes;
                    existingActivity.UpdatedAt = DateTime.UtcNow;
                    _logger.LogInformation($"Updated activity summary: +{req.TotalCalories} calories");
                }
                else
                {
                    _context.ActActivitySummaries.Add(new ActActivitySummary
                    {
                        UserId = userId.Value,
                        CaloriesBurned = req.TotalCalories,
                        TotalMinutes = req.TotalMinutes,
                        LogDate = todayDateOnly,
                        UpdatedAt = DateTime.UtcNow
                    });
                    _logger.LogInformation($"Created activity summary: {req.TotalCalories} calories");
                }
            }

            // 7. Save all changes before advancing day
            await _context.SaveChangesAsync();
            _logger.LogInformation($"Saved changes for Day {activeProgram.CurrentDayNo}");

            // 8. Advance to Next Day
            int previousDay = activeProgram.CurrentDayNo;

            if (activeProgram.CurrentDayNo < 28)
            {
                activeProgram.CurrentDayNo += 1;
                _logger.LogInformation($"Advanced from Day {previousDay} to Day {activeProgram.CurrentDayNo}");
            }
            else
            {
                activeProgram.Status = "COMPLETED";
                activeProgram.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation($"Program completed at Day 28");
            }

            // 9. Save program advancement
            await _context.SaveChangesAsync();

            // 10. Prepare response message
            string message;
            string skipMessage = null;

            if (isSkipped)
            {
                message = $"Day {previousDay} skipped. Moving to Day {activeProgram.CurrentDayNo}. Remember to stay consistent! 💪";
                skipMessage = "Try not to skip too many days for best results.";
            }
            else if (isRestDay)
            {
                message = $"Rest day completed! Moving to Day {activeProgram.CurrentDayNo}. Enjoy your recovery! 🧘";
            }
            else
            {
                message = $"🎉 Great job! Day {previousDay} completed. Ready for Day {activeProgram.CurrentDayNo}! 💪";
            }

            var result = new WorkoutSessionResultDto
            {
                Message = message,
                CurrentDay = previousDay,
                NextDay = activeProgram.CurrentDayNo,
                IsProgramFinished = activeProgram.Status == "COMPLETED",
                Status = isSkipped ? "SKIPPED" : (isRestDay ? "REST_COMPLETED" : "COMPLETED"),
                WasSkipped = isSkipped,
                SkipMessage = skipMessage
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
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo);

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

    private async Task EnsureCalendarEntryExists(UsrUserProgramInstance activeProgram, int dayNo, bool isRestDay)
    {
        var existingCalendar = await _context.NtrMealPlanCalendars
            .FirstOrDefaultAsync(c => c.CycleId == activeProgram.CycleNo && c.DayNo == dayNo);

        if (existingCalendar == null)
        {
            var calendarEntry = new NtrMealPlanCalendar
            {
                CycleId = activeProgram.CycleNo,
                PlanDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(dayNo - activeProgram.CurrentDayNo)),
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

    private async Task CreateSessionWorkouts(int sessionId, int programId, string dayType)
    {
        _logger.LogInformation($"Creating session workouts for Session {sessionId}, Program {programId}, DayType {dayType}");

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

        // ✅ STEP 1: ALWAYS ADD 2 RANDOM WARMUPS
        var warmups = await _context.WrkWorkouts
            .Where(w => w.Category != null
                     && w.Category.ToUpper() == "WARMUP"
                     && w.IsActive == true)
            .OrderBy(w => Guid.NewGuid())
            .Take(2)
            .ToListAsync();

        _logger.LogInformation($"Adding {warmups.Count} warmup exercises");

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

        // ✅ STEP 2: Add main workouts from template
        var templateWorkouts = await _context.WrkProgramTemplateDaytypeWorkouts
            .Where(tw => tw.ProgramId == programId && tw.DayType == dayType)
            .OrderBy(tw => tw.WorkoutOrder)
            .Take(8) // Max 8 main workouts
            .ToListAsync();

        if (!templateWorkouts.Any())
        {
            _logger.LogWarning($"No template workouts found for {dayType}, using fallback");

            // Fallback: Get random workouts (excluding warmups)
            var fallbackWorkouts = await _context.WrkWorkouts
                .Where(w => w.IsActive == true
                         && w.Category != null
                         && w.Category.ToUpper() != "WARMUP"
                         && w.Category.ToUpper() != "CARDIO")
                .OrderBy(w => Guid.NewGuid())
                .Take(8)
                .ToListAsync();

            foreach (var workout in fallbackWorkouts)
            {
                sessionWorkouts.Add(new UsrUserSessionWorkout
                {
                    SessionId = sessionId,
                    WorkoutId = workout.WorkoutId,
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

        // Verify no duplicate order numbers
        var duplicateOrders = sessionWorkouts
            .GroupBy(w => w.OrderNo)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateOrders.Any())
        {
            _logger.LogWarning($"Duplicate orders detected: {string.Join(",", duplicateOrders)}. Rebuilding...");
            for (int i = 0; i < sessionWorkouts.Count; i++)
            {
                sessionWorkouts[i].OrderNo = i + 1;
            }
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

        return new WorkoutExerciseDto
        {
            Id = sw.WorkoutId,
            Name = sw.Workout.WorkoutName ?? "Unknown Exercise",
            ImageFileName = !string.IsNullOrEmpty(sw.Workout.ImgFilename)
                ? $"{baseUrl}/images/workouts/{GetCategoryFolder(sw.Workout.Category)}/{sw.Workout.ImgFilename}"
                : $"{baseUrl}/images/workouts/default.png",
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