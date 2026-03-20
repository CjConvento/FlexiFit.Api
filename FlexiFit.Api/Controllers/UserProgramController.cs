using FlexiFit.Api.Entities;
using FlexiFit.Api.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserProgramController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<UserProgramController> _logger;

        public UserProgramController(FlexiFitDbContext context, ILogger<UserProgramController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("check-progression")]
        public async Task<IActionResult> CheckProgression()
        {
            var userId = GetUserId();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return NotFound(new { message = "Babe, walang active program found." });

            return await ProcessProgressionLogic(activeProgram);
        }

        [HttpPost("debug/fast-forward-to-end")]
        public async Task<IActionResult> FastForwardToEnd()
        {
            var userId = GetUserId();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null) return NotFound("Walang active program, babe.");

            // I-set sa Day 29 para ma-trigger ang level up logic
            activeProgram.CurrentDayNo = 29;
            await _context.SaveChangesAsync();

            _logger.LogWarning($"CCTV: Fast-forwarding Instance {activeProgram.InstanceId} to Day 29.");

            return await ProcessProgressionLogic(activeProgram);
        }

        private async Task<IActionResult> HandleLevelUp(UsrUserProgramInstance activeProgram)
        {
            // 1. Kunin ang Profile
            var profile = await _context.UsrUserProfileVersions
                .FirstOrDefaultAsync(p => p.UserId == activeProgram.UserId && p.IsCurrent == true);
            if (profile == null) return BadRequest("Profile missing.");

            // 2. Determine Next Level
            string currentLevel = (profile.FitnessLevelSelected ?? "BEGINNER").Trim().ToUpper();
            string nextLevel = currentLevel switch
            {
                "BEGINNER" => "INTERMEDIATE",
                "INTERMEDIATE" => "ADVANCED",
                _ => "ADVANCED"
            };

            // 3. Hanapin ang tamang Template (e.g., Program 153 for Intermediate)
            var nextTemplate = await _context.WrkProgramTemplates
                .Where(t => t.IsActive == true && t.FitnessLevel.ToUpper() == nextLevel)
                .OrderByDescending(t => t.ProgramId)
                .FirstOrDefaultAsync();

            if (nextTemplate == null) return BadRequest($"No template for {nextLevel}");

            // 🔥 DITO ANG SIKRETO: Burahin lahat ng lumang sessions ng instance na ito
            var oldSessions = _context.UsrUserWorkoutSessions.Where(s => s.ProgramInstanceId == activeProgram.InstanceId);
            _context.UsrUserWorkoutSessions.RemoveRange(oldSessions);

            var oldInstances = _context.UsrUserSessionInstances.Where(i => i.InstanceId == activeProgram.InstanceId);
            _context.UsrUserSessionInstances.RemoveRange(oldInstances);

            // 4. Update Level sa Profile at Instance
            string formattedLevel = nextLevel.Substring(0, 1).ToUpper() + nextLevel.Substring(1).ToLower();
            profile.FitnessLevelSelected = formattedLevel;
            activeProgram.ProgramId = nextTemplate.ProgramId;
            activeProgram.FitnessLevelAtStart = formattedLevel;
            activeProgram.CurrentDayNo = 1;

            await _context.SaveChangesAsync();

            // 5. Generate New Intermediate Slots
            await GenerateProgramSlotsForNewCycle(activeProgram.InstanceId, activeProgram.UserId, activeProgram.ProgramId);

            return Ok(new { message = $"Level Up to {formattedLevel} Success! 🚀" });
        }

        private async Task GenerateProgramSlotsForNewCycle(int instanceId, int userId, int programId)
        {
            _context.ChangeTracker.Clear();

            // Kunin ang template structure para sa Day 1 to 28
            var templateDays = await _context.WrkProgramTemplateDays
                .AsNoTracking()
                .Where(td => td.ProgramId == programId)
                .ToListAsync();

            for (int day = 1; day <= 28; day++)
            {
                var workoutForThisDay = templateDays.FirstOrDefault(td => td.DayNo == day);
                if (workoutForThisDay == null || workoutForThisDay.DayType == "REST") continue;

                // 1. Create Main Session
                var workoutSession = new UsrUserWorkoutSession
                {
                    UserId = userId,
                    ProgramInstanceId = instanceId,
                    WorkoutDay = day,
                    Status = "PENDING",
                    CreatedAt = DateTime.Now
                };
                _context.UsrUserWorkoutSessions.Add(workoutSession);
                await _context.SaveChangesAsync();

                // 2. 🔥 THE FIX: Join sa ProgramId para makuha ang tamang Level exercises
                var exercises = await _context.WrkProgramTemplateDaytypeWorkouts
                    .AsNoTracking()
                    .Where(w => w.ProgramId == programId && w.DayType == workoutForThisDay.DayType)
                    .OrderBy(w => w.WorkoutOrder)
                    .Take(8)
                    .ToListAsync();

                var sessionWorkouts = exercises.Select(ex => new UsrUserSessionWorkout
                {
                    SessionId = workoutSession.SessionId,
                    WorkoutId = ex.WorkoutId, // Dito papasok ang Intermediate IDs (7, 9, 12, etc.)
                    Sets = ex.SetsDefault,
                    Reps = ex.RepsDefault,
                    LoadKg = 0,
                    OrderNo = ex.WorkoutOrder
                }).ToList();

                _context.UsrUserSessionWorkouts.AddRange(sessionWorkouts);
            }
            await _context.SaveChangesAsync();
        }
        private int GetUserId()
        {
            var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return claim != null ? int.Parse(claim) : 0;
        }

        private async Task<IActionResult> ProcessProgressionLogic(UsrUserProgramInstance activeProgram)
        {
            if (activeProgram.CurrentDayNo > 28)
            {
                _logger.LogInformation($"CCTV: Starting Level Up for Instance {activeProgram.InstanceId}...");
                return await HandleLevelUp(activeProgram);
            }

            return Ok(new
            {
                message = "Consistency is key! Laban lang.",
                currentDay = activeProgram.CurrentDayNo,
                daysRemaining = 28 - activeProgram.CurrentDayNo + 1
            });
        }
    }
}