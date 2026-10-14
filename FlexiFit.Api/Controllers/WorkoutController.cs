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
    private readonly WorkoutService _workoutService;

    public WorkoutController(
        FlexiFitDbContext context, 
        ILogger<WorkoutController> logger,
        WorkoutService workoutService)
    {
        _context = context;
        _logger = logger;
        _workoutService = workoutService;
    }

    [HttpGet("today")]
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetTodayWorkout([FromQuery] int programNumber = 1)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // DELEGATED TO SERVICE (Shrunk from ~200 lines to 4 lines)
            var response = await _workoutService.GetTodayWorkoutAsync(userId.Value, programNumber, baseUrl);

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
            return StatusCode(500, new { message = "An error occurred while retrieving your workout plan." });
        }
    }

    [HttpPost("complete")]
    public async Task<ActionResult<WorkoutSessionResultDto>> CompleteSession([FromBody] WorkoutSessionCompleteDto req)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        try
        {
            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            // DELEGATED TO SERVICE (Shrunk from ~150 lines to 4 lines)
            var result = await _workoutService.CompleteSessionAsync(userId.Value, req, todayDateOnly);

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

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // DELEGATED TO SERVICE (From ~80 lines down to 1 line)
            var response = await _workoutService.GetWorkoutByDateAsync(userId.Value, day, month, programNumber, baseUrl);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
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
  
}