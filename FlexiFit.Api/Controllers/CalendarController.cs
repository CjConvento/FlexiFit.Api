using FlexiFit.Api.Entities;
using FlexiFit.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FlexiFit.Api.Services;
using FlexiFit.Api.Extensions;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/calendar")]
public class CalendarController : ControllerBase
{
    private readonly ILogger<CalendarController> _logger;
    private readonly CalendarService _calendarService;

    public CalendarController(
        ILogger<CalendarController> logger,
        CalendarService calendarService
        )
    {
        _logger = logger;
        _calendarService = calendarService;
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetCalendarHistory()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        _logger.LogInformation($"Getting calendar history for user {userId}");

        // Tawagin lang ang service. Kung walang active program, magtthrow ng 404 via Middleware.
        var history = await _calendarService.GetCalendarHistoryAsync(userId.Value);
        return Ok(history);
    }

    [HttpGet("week/{weekNo}")]
    public async Task<IActionResult> GetWeekSchedule(int weekNo)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var schedule = await _calendarService.GetWeekScheduleAsync(userId.Value, weekNo);
        return Ok(schedule);
    }

    [HttpGet("day/{dayNo}")]
    public async Task<IActionResult> GetDayDetails(int dayNo)
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var dayDetails = await _calendarService.GetDayDetailsAsync(userId.Value, dayNo);
        return Ok(dayDetails);
    }

    [HttpGet("progress")]
    public async Task<IActionResult> GetProgramProgress()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var progress = await _calendarService.GetProgramProgressAsync(userId.Value);
        return Ok(progress);
    }

    [HttpGet("full-schedule")]
    public async Task<IActionResult> GetFullSchedule()
    {
        var userId = User.GetUserId();
        if (userId == null) return Unauthorized();

        var fullSchedule = await _calendarService.GetFullScheduleAsync(userId.Value);
        return Ok(fullSchedule);
    }
}