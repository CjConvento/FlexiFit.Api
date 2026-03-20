using FlexiFit.Api.Entities;
using FlexiFit.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

        public async Task<IActionResult> GetCalendarHistory()
        {


            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
            {
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

            {




                var dto = new CalendarHistoryDto
                {
                };



                {
                    {
                    }
                    {
                    }
                    {
                    }

    #region Helper Methods

    private string GetDaySummary(string dayType, int weekNo)
    {
        if (dayType.Contains("REST", StringComparison.OrdinalIgnoreCase))
            return "Rest Day";

        return $"{dayType} - Week {weekNo}";
                }
                {
                }

            }

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