using FlexiFit.Api.Entities;
using FlexiFit.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CalendarController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<CalendarController> _logger;

        public CalendarController(FlexiFitDbContext context, ILogger<CalendarController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("history-map")]
        public async Task<IActionResult> GetCalendarHistory()
        {
            _logger.LogInformation("CCTV: Requesting Calendar History Map...");

            // 1. Get User ID from Token
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr)) return Unauthorized();
            int userId = int.Parse(userIdStr);

            // 2. Get Active Program
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
            {
                _logger.LogWarning($"CCTV: No active program for User {userId}");
                return NotFound(new { message = "No active program found." });
            }

            var historyList = new List<CalendarHistoryDto>();

            // 3. MASTER LOOP: Day 1 to 28
            for (int i = 1; i <= 28; i++)
            {
                // 🔥 MODULUS MAGIC: Re-use Day 1-7 template for all weeks
                int templateDayNo = ((i - 1) % 7) + 1;

                var dayDef = await _context.WrkProgramTemplateDays
                    .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo);

                // Fetch session records for this specific day (1-28)
                var workoutSession = await _context.UsrUserSessionInstances
                    .FirstOrDefaultAsync(s => s.InstanceId == activeProgram.InstanceId && s.DayNo == i);

                var nutritionSession = await _context.NtrMealPlanCalendars
                    .FirstOrDefaultAsync(m => m.CycleId == activeProgram.CycleNo && m.DayNo == i);

                var dto = new CalendarHistoryDto
                {
                    Day = i,
                    Week = ((i - 1) / 7) + 1,
                    DayType = dayDef?.DayType?.ToUpper() ?? "WORKOUT",
                    Summary = dayDef?.DayType?.Replace("_", " ") ?? "Training Day"
                };

                // --- A. WORKOUT STATUS LOGIC ---
                if (workoutSession != null)
                    dto.WorkoutStatus = workoutSession.Status.ToUpper();
                else if (i == activeProgram.CurrentDayNo)
                    dto.WorkoutStatus = "PENDING";
                else if (i < activeProgram.CurrentDayNo)
                    dto.WorkoutStatus = "SKIPPED";
                else
                    dto.WorkoutStatus = "NOT STARTED";

                // --- B. NUTRITION STATUS LOGIC ---
                if (nutritionSession != null)
                    dto.NutritionStatus = nutritionSession.Status.ToUpper();
                else if (i == activeProgram.CurrentDayNo)
                    dto.NutritionStatus = "PENDING";
                else if (i < activeProgram.CurrentDayNo)
                    dto.NutritionStatus = "SKIPPED";
                else
                    dto.NutritionStatus = "NOT STARTED";

                // --- C. OVERALL STATUS LOGIC ---
                // Special handling for REST days
                if (dto.DayType.Contains("REST"))
                {
                    if (i < activeProgram.CurrentDayNo)
                    {
                        dto.Status = "COMPLETED";
                        dto.WorkoutStatus = "COMPLETED";
                        dto.NutritionStatus = "COMPLETED";
                    }
                    else if (i == activeProgram.CurrentDayNo)
                    {
                        dto.Status = "PENDING"; // Or COMPLETED if you want rest days auto-done
                    }
                    else
                    {
                        dto.Status = "NOT STARTED";
                    }
                }
                else
                {
                    // For workout days, follow the workout session status
                    dto.Status = dto.WorkoutStatus;
                }

                historyList.Add(dto);
            }

            _logger.LogInformation($"CCTV: Map generated for User {userId}, Cycle {activeProgram.CycleNo}");
            return Ok(historyList);
        }
    }
}