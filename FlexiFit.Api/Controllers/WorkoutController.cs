using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/workout")]
    public class WorkoutController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;

        public WorkoutController(FlexiFitDbContext context)
        {
            _context = context;
        }

        [HttpGet("today")]
        public async Task<ActionResult<DailyWorkoutPlanDto>> GetTodayWorkout()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null)
                return NotFound(new { message = "No active program found." });

            var template = await _context.WrkProgramTemplates
                .FirstOrDefaultAsync(t => t.ProgramId == activeProgram.ProgramId);

            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId
                                       && d.DayNo == activeProgram.CurrentDayNo);

            if (dayDef == null) return NotFound(new { message = "Workout day details not found." });

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var response = new DailyWorkoutPlanDto
            {
                // FIX CS0019: Inalis ang ?? 0 dahil int na ang DayNo
                DayNo = dayDef.DayNo,
                DayType = dayDef.DayType,
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = template?.ProgramName ?? "FlexiFit Program",
                    Status = activeProgram.Status,
                    // FIX CS0019: Inalis ang ?? 0
                    Day = dayDef.DayNo
                }
            };

            if (dayDef.DayType != null && dayDef.DayType.ToUpper().Contains("REST"))
            {
                response.Message = "Recovery day! Enjoy your rest, babe! ✨";
                return Ok(response);
            }
            // Warmups
            response.Warmups = await _context.WrkWorkouts
                .Where(w => w.Category == "WARMUP" && w.IsActive == true)
                .Where(w => w.MuscleGroup == dayDef.DayType || w.MuscleGroup == "WHOLE_BODY")
                .OrderBy(r => Guid.NewGuid()).Take(2)
                .Select(w => new WorkoutExerciseDto
                {
                    Id = w.WorkoutId,
                    Name = w.WorkoutName,
                    ImageFileName = !string.IsNullOrEmpty(w.ImgFilename) ? $"{baseUrl}/uploads/images/{w.ImgFilename}" : "",
                    // FIX HERE: Added cast and null check
                    Calories = (int)(w.CaloriesBurned ?? 0)
                }).ToListAsync();

            // Main Workouts - FIX CS0266: Explicit casting (int)
            int currentStep = ((int)activeProgram.CurrentDayNo <= 14) ? 1 : 2;

            response.Workouts = await (from dw in _context.WrkProgramTemplateDaytypeWorkouts
                                       join w in _context.WrkWorkouts on dw.WorkoutId equals w.WorkoutId
                                       join ls in _context.WrkWorkoutLoadSteps on w.WorkoutId equals ls.WorkoutId
                                       where dw.ProgramId == activeProgram.ProgramId
                                             && dw.DayType == dayDef.DayType
                                             && ls.StepNo == currentStep
                                       orderby dw.WorkoutOrder
                                       select new WorkoutExerciseDto
                                       {
                                           Id = w.WorkoutId,
                                           Name = w.WorkoutName,
                                           Sets = dw.SetsDefault,
                                           Reps = dw.RepsDefault,
                                           Calories = (int)(w.CaloriesBurned ?? 0)
                                       }).ToListAsync();

            return Ok(response);
        }

        // Ito yung bago para sa Calendar (Specific Day)
        [HttpGet("plan/{dayNo}")]
        public async Task<ActionResult<DailyWorkoutPlanDto>> GetWorkoutByDay(int dayNo)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            // 1. Kunin ang active program
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null) return NotFound(new { message = "No active program." });

            // 2. Hanapin ang definition ng pinindot na dayNo sa calendar
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId
                                       && d.DayNo == dayNo);

            if (dayDef == null) return NotFound(new { message = "Day definition not found." });

            // 3. Tignan sa logs kung tapos na ba itong session na ito (Status check)
            var sessionLog = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == dayNo);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var response = new DailyWorkoutPlanDto
            {
                DayNo = dayDef.DayNo,
                DayType = dayDef.DayType,
                // Status indicator para malaman ng Android kung DONE, SKIPPED, o PENDING
                Message = sessionLog != null ? "DONE" : (dayNo < activeProgram.CurrentDayNo ? "SKIPPED" : "PENDING"),
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    Day = dayDef.DayNo
                }
            };

            if (dayDef.DayType != null && dayDef.DayType.ToUpper().Contains("REST"))
            {
                return Ok(response);
            }

            // Same logic: Step 1 (Day 1-14) or Step 2 (Day 15-28)
            int currentStep = (dayNo <= 14) ? 1 : 2;

            response.Workouts = await (from dw in _context.WrkProgramTemplateDaytypeWorkouts
                                       join w in _context.WrkWorkouts on dw.WorkoutId equals w.WorkoutId
                                       join ls in _context.WrkWorkoutLoadSteps on w.WorkoutId equals ls.WorkoutId
                                       where dw.ProgramId == activeProgram.ProgramId
                                             && dw.DayType == dayDef.DayType
                                             && ls.StepNo == currentStep
                                       orderby dw.WorkoutOrder
                                       select new WorkoutExerciseDto
                                       {
                                           Id = w.WorkoutId,
                                           Name = w.WorkoutName,
                                           Sets = dw.SetsDefault,
                                           Reps = dw.RepsDefault,
                                           Calories = (int)(w.CaloriesBurned ?? 0)
                                       }).ToListAsync();

            return Ok(response);
        }

        [HttpPost("complete")]
        public async Task<IActionResult> CompleteSession([FromBody] WorkoutSessionCompleteDto req)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null) return BadRequest("No active program found.");

            // 1. I-save ang session (Kahit SKIPPED o DONE)
            var newSession = new UsrUserWorkoutSession
            {
                UserId = userId.Value,
                ProgramInstanceId = activeProgram.InstanceId,
                WorkoutDay = (short)activeProgram.CurrentDayNo,
                // Dito natin malalaman kung "DONE", "SKIPPED", o "REST_COMPLETED"
                Status = req.Status ?? "DONE",
                StartedAt = DateTime.UtcNow.AddMinutes(-req.TotalMinutes),
                CompletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            // Kung hindi naman iniskip, i-log ang activity summary
            if (newSession.Status != "SKIPPED")
            {
                _context.ActActivitySummaries.Add(new ActActivitySummary
                {
                    UserId = userId.Value,
                    CaloriesBurned = (short)req.TotalCalories,
                    TotalMinutes = (short)req.TotalMinutes,
                    LogDate = DateOnly.FromDateTime(DateTime.UtcNow)
                });
            }

            _context.UsrUserWorkoutSessions.Add(newSession);

            // 2. Progression Logic (The 28-day cycle)
            if (activeProgram.CurrentDayNo < 28)
            {
                activeProgram.CurrentDayNo += 1;
            }
            else
            {
                // LOGIC PARA SA LEVEL UP O CYCLE REPEAT
                await HandleLevelProgression(activeProgram);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Progress Saved!", nextDay = activeProgram.CurrentDayNo });
        }

        private async Task HandleLevelProgression(UsrUserProgramInstance activeProgram)
        {
            // 1. Kunin ang current profile snapshot ng user
            var profile = await _context.UsrUserProfileVersions
                .FirstOrDefaultAsync(p => p.UserId == activeProgram.UserId && p.IsCurrent == true);

            if (profile == null) return;

            // 2. Logic para sa Next Level
            string currentLevel = profile.FitnessLevelSelected?.ToUpper() ?? "BEGINNER";
            string nextLevel = currentLevel switch
            {
                "BEGINNER" => "INTERMEDIATE",
                "INTERMEDIATE" => "ADVANCED",
                "ADVANCED" => "ADVANCED", // Cycle repeat mode
                _ => "BEGINNER"
            };

            // 3. I-update ang level sa user profile para reflected sa UI
            profile.FitnessLevelSelected = nextLevel;

            // 4. Humanap ng bagong Program Template gamit ang mga filters sa entity mo
            // Gagamitin natin yung IX_wrk_program_templates_filter na index mo para mabilis ang query
            var nextProgram = await _context.WrkProgramTemplates
                .Where(t => t.IsActive == true)
                .Where(t => t.FitnessLevel == nextLevel)
                .Where(t => t.ProgramCategory == profile.GoalSelected)
                // Dito natin kukunin yung logic mo na if same level (Advanced), 
                // pwedeng random order para hindi paulit-ulit
                .OrderBy(r => Guid.NewGuid())
                .FirstOrDefaultAsync();

            if (nextProgram != null)
            {
                activeProgram.ProgramId = nextProgram.ProgramId;
            }

            // 5. Reset the clock! Back to Day 1 for the next 28-day cycle
            activeProgram.CurrentDayNo = 1;
        }

        private int? GetUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("user_id")?.Value;
            if (int.TryParse(userIdClaim, out var id)) return id;
            return null;
        }
    }
}