using FlexiFit.Api.Dtos;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Extensions;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/workout")]
public class WorkoutController : ControllerBase
{
    private readonly ILogger<WorkoutController> _logger;
    private readonly WorkoutService _workoutService;

    public WorkoutController(
        ILogger<WorkoutController> logger,
        WorkoutService workoutService)
    {
        _logger = logger;
        _workoutService = workoutService;
    }

    [HttpGet("today")]
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetTodayWorkout([FromQuery] int programNumber = 1)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        // DELEGATED TO SERVICE (Shrunk from ~200 lines to 4 lines)
        var response = await _workoutService.GetTodayWorkoutAsync(userId.Value, programNumber, baseUrl);

        return Ok(response);
    }

    [HttpPost("complete")]
    public async Task<ActionResult<WorkoutSessionResultDto>> CompleteSession([FromBody] WorkoutSessionCompleteDto req)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        // DELEGATED TO SERVICE (Shrunk from ~150 lines to 4 lines)
        var result = await _workoutService.CompleteSessionAsync(userId.Value, req, todayDateOnly);

        return Ok(result);
    }

    [HttpGet("can-skip")]
    public async Task<IActionResult> CanSkipToday()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        var response = await _workoutService.CanSkipTodayAsync(userId.Value);
        return Ok(response);
    }

    [HttpGet("history-detail")]
    public async Task<ActionResult<DailyWorkoutPlanDto>> GetWorkoutByDate([FromQuery] int day, [FromQuery] int month, [FromQuery] int programNumber = 1)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var response = await _workoutService.GetWorkoutByDateAsync(userId.Value, day, month, programNumber, baseUrl);
        return Ok(response);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetWorkoutHistory()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        var response = await _workoutService.GetWorkoutHistoryAsync(userId.Value);
        return Ok(response);
    }

    [HttpGet("session/{sessionId}")]
    public async Task<IActionResult> GetSessionDetails(int sessionId)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

        // Kung wala ang session, magtthrow ang Service ng KeyNotFoundException (404 via Middleware)
        var response = await _workoutService.GetSessionDetailsAsync(sessionId, userId.Value);
        return Ok(response);
    }
}