using FlexiFit.Api.Entities;
using FlexiFit.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/calendar")]
public class CalendarController : ControllerBase
{
    private readonly FlexiFitDbContext _context;
    private readonly ILogger<CalendarController> _logger;

    public CalendarController(FlexiFitDbContext context, ILogger<CalendarController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetCalendarHistory()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        _logger.LogInformation($"Getting calendar history for user {userId}");

        try
        {
            // 1. Get Active Program
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
            {
                _logger.LogWarning($"No active program found for user {userId}");
                return NotFound(new { message = "No active program found." });
            }

            _logger.LogInformation($"Active Program: ProgramId={activeProgram.ProgramId}, CurrentDay={activeProgram.CurrentDayNo}");

            // 2. Get Template Days Pattern (Week 1 - Days 1-7)
            var templateDays = await _context.WrkProgramTemplateDays
                .Where(d => d.ProgramId == activeProgram.ProgramId)
                .OrderBy(d => d.DayNo)
                .Select(d => new { d.DayNo, d.DayType, d.Notes })
                .ToListAsync();

            // Create dictionary for quick lookup
            var templateMap = templateDays.ToDictionary(d => d.DayNo, d => d.DayType);

            _logger.LogInformation($"Loaded {templateMap.Count} template days (Week 1 pattern)");

            // 3. Get User Workout Sessions
            var workoutSessions = await _context.UsrUserWorkoutSessions
                .Where(s => s.ProgramInstanceId == activeProgram.InstanceId)
                .ToListAsync();

            var workoutMap = workoutSessions
                .GroupBy(s => s.WorkoutDay)
                .Select(g => g.OrderByDescending(s => s.CreatedAt).First())
                .ToDictionary(s => s.WorkoutDay, s => s);

            // 4. Get Nutrition Calendar Entries
            var nutritionDays = await _context.NtrMealPlanCalendars
                .Where(c => c.CycleId == activeProgram.CycleNo)
                .ToListAsync();

            var nutritionMap = nutritionDays
                .GroupBy(c => c.DayNo)
                .Select(g => g.OrderByDescending(c => c.UpdatedAt).First())
                .ToDictionary(c => c.DayNo, c => c);

            var historyList = new List<CalendarHistoryDto>();

            // 5. Generate 28 Days (4 Weeks x 7 Days)
            for (int day = 1; day <= 28; day++)
            {
                // Map to template day (1-7 pattern that repeats)
                int templateDayNo = ((day - 1) % 7) + 1;

                // Get day type from Week 1 pattern
                string dayType = templateMap.GetValueOrDefault(templateDayNo, "WORKOUT");

                // Calculate week number (1-4)
                int weekNo = ((day - 1) / 7) + 1;

                // Get user data for this specific day
                workoutMap.TryGetValue(day, out var workoutSession);
                nutritionMap.TryGetValue(day, out var nutritionDay);

                bool isRestDay = dayType.Contains("REST", StringComparison.OrdinalIgnoreCase);

                var dto = new CalendarHistoryDto
                {
                    Day = day,
                    Week = weekNo,
                    DayType = dayType,
                    Summary = GetDaySummary(dayType, weekNo),
                    WorkoutStatus = GetWorkoutStatus(workoutSession, day, activeProgram.CurrentDayNo, isRestDay),
                    NutritionStatus = GetNutritionStatus(nutritionDay, day, activeProgram.CurrentDayNo),
                    Status = GetOverallStatus(workoutSession, day, activeProgram.CurrentDayNo, isRestDay)
                };

                historyList.Add(dto);
            }

            _logger.LogInformation($"Generated {historyList.Count} calendar days (4 weeks)");
            return Ok(historyList);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating calendar history");
            return StatusCode(500, new
            {
                message = "An error occurred while generating calendar",
                error = ex.Message
            });
        }
    }

    [HttpGet("week/{weekNo}")]
    public async Task<IActionResult> GetWeekSchedule(int weekNo)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var activeProgram = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        if (activeProgram == null)
            return NotFound("No active program");

        if (weekNo < 1 || weekNo > 4)
            return BadRequest("Week must be between 1 and 4");

        // Get template pattern (Week 1)
        var templateDays = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId)
            .OrderBy(d => d.DayNo)
            .Select(d => new { d.DayNo, d.DayType })
            .ToListAsync();

        var weekDays = new List<object>();
        int startDay = (weekNo - 1) * 7 + 1;

        for (int i = 0; i < 7; i++)
        {
            var template = templateDays[i % 7];
            int actualDay = startDay + i;

            weekDays.Add(new
            {
                day = actualDay,
                templateDay = template.DayNo,
                dayType = template.DayType,
                week = weekNo
            });
        }

        return Ok(new
        {
            week = weekNo,
            startDay = startDay,
            endDay = startDay + 6,
            days = weekDays
        });
    }

    [HttpGet("day/{dayNo}")]
    public async Task<IActionResult> GetDayDetails(int dayNo)
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var activeProgram = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        if (activeProgram == null)
            return NotFound("No active program");

        // Map to template day
        int templateDayNo = ((dayNo - 1) % 7) + 1;

        var templateDay = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo)
            .Select(d => new { d.DayType, d.Notes })
            .FirstOrDefaultAsync();

        var workoutSession = await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId && s.WorkoutDay == dayNo)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        var nutritionDay = await _context.NtrMealPlanCalendars
            .Where(c => c.CycleId == activeProgram.CycleNo && c.DayNo == dayNo)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            day = dayNo,
            week = ((dayNo - 1) / 7) + 1,
            templateDay = templateDayNo,
            dayType = templateDay?.DayType ?? "WORKOUT",
            notes = templateDay?.Notes,
            workout = workoutSession != null ? new
            {
                sessionId = workoutSession.SessionId,
                status = workoutSession.Status,
                completedAt = workoutSession.CompletedAt,
                startedAt = workoutSession.StartedAt
            } : null,
            nutrition = nutritionDay != null ? new
            {
                status = nutritionDay.Status,
                updatedAt = nutritionDay.UpdatedAt
            } : null,
            isCurrentDay = dayNo == activeProgram.CurrentDayNo,
            isPastDay = dayNo < activeProgram.CurrentDayNo,
            isFutureDay = dayNo > activeProgram.CurrentDayNo
        });
    }

    [HttpGet("progress")]
    public async Task<IActionResult> GetProgramProgress()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var activeProgram = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        if (activeProgram == null)
            return NotFound("No active program");

        var completedWorkouts = await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId && s.Status == "COMPLETED")
            .Select(s => s.WorkoutDay)
            .Distinct()
            .CountAsync();

        var completedNutrition = await _context.NtrMealPlanCalendars
            .Where(c => c.CycleId == activeProgram.CycleNo && c.Status == "DONE")
            .Select(c => c.DayNo)
            .Distinct()
            .CountAsync();

        int currentWeek = ((activeProgram.CurrentDayNo - 1) / 7) + 1;
        int currentDayInWeek = ((activeProgram.CurrentDayNo - 1) % 7) + 1;

        return Ok(new
        {
            currentDay = activeProgram.CurrentDayNo,
            currentWeek = currentWeek,
            currentDayInWeek = currentDayInWeek,
            totalDays = 28,
            totalWeeks = 4,
            completedWorkouts = completedWorkouts,
            completedNutrition = completedNutrition,
            progress = Math.Round((double)(activeProgram.CurrentDayNo - 1) / 28 * 100, 1),
            programStatus = activeProgram.Status
        });
    }

    [HttpGet("full-schedule")]
    public async Task<IActionResult> GetFullSchedule()
    {
        var userId = GetUserId();
        if (userId == null) return Unauthorized();

        var activeProgram = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        if (activeProgram == null)
            return NotFound("No active program");

        // Get template pattern
        var templateDays = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId)
            .OrderBy(d => d.DayNo)
            .Select(d => new { d.DayNo, d.DayType })
            .ToListAsync();

        var fullSchedule = new List<object>();

        for (int week = 1; week <= 4; week++)
        {
            var weekDays = new List<object>();
            int startDay = (week - 1) * 7 + 1;

            for (int i = 0; i < 7; i++)
            {
                var template = templateDays[i % 7];
                int actualDay = startDay + i;

                weekDays.Add(new
                {
                    day = actualDay,
                    dayType = template.DayType,
                    isRestDay = template.DayType.Contains("REST")
                });
            }

            fullSchedule.Add(new
            {
                week = week,
                days = weekDays
            });
        }

        return Ok(new
        {
            programId = activeProgram.ProgramId,
            programName = activeProgram.Program?.ProgramName,
            schedule = fullSchedule,
            note = "Weeks 2, 3, and 4 repeat the pattern from Week 1"
        });
    }

    #region Helper Methods

    private string GetDaySummary(string dayType, int weekNo)
    {
        if (dayType.Contains("REST", StringComparison.OrdinalIgnoreCase))
            return "Rest Day";

        return $"{dayType} - Week {weekNo}";
    }

    private string GetWorkoutStatus(UsrUserWorkoutSession? session, int day, int currentDay, bool isRestDay)
    {
        if (session != null)
            return session.Status.ToUpper();

        if (isRestDay && day <= currentDay)
            return "COMPLETED";

        if (day < currentDay)
            return "SKIPPED";

        if (day == currentDay)
            return "PENDING";

        return "NOT_STARTED";
    }

    private string GetNutritionStatus(NtrMealPlanCalendar? nutritionDay, int day, int currentDay)
    {
        if (nutritionDay != null)
            return nutritionDay.Status.ToUpper();

        if (day < currentDay)
            return "SKIPPED";

        if (day == currentDay)
            return "PENDING";

        return "NOT_STARTED";
    }

    private string GetOverallStatus(UsrUserWorkoutSession? session, int day, int currentDay, bool isRestDay)
    {
        if (isRestDay)
            return day <= currentDay ? "COMPLETED" : "NOT_STARTED";

        if (session?.Status?.ToUpper() == "COMPLETED")
            return "COMPLETED";

        if (day < currentDay)
            return "SKIPPED";

        if (day == currentDay)
            return "PENDING";

        return "NOT_STARTED";
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