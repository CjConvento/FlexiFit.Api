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

            var sessionLog = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == (short)activeProgram.CurrentDayNo);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            string currentStatus = sessionLog?.Status ?? "Pending";

            // 1. Initialize Response (Kasama na yung bagong fields para sa Android)
            var response = new DailyWorkoutPlanDto
            {
                DayNo = dayDef.DayNo,
                DayType = dayDef.DayType ?? "Training",
                Message = currentStatus,
                FocusArea = dayDef.DayType, // Para sa focusArea sa Android
                Level = template?.FitnessLevel, // Para sa level sa Android
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = template?.ProgramName ?? "FlexiFit Program",
                    Description = template?.Description ?? "",
                    Level = template?.FitnessLevel ?? "",
                    Status = currentStatus,
                    Day = dayDef.DayNo
                }
            };

            if (dayDef.DayType != null && dayDef.DayType.ToUpper().Contains("REST"))
            {
                response.Message = "Recovery day! Enjoy your rest, babe! ✨";
                return Ok(response);
            }

            // 2. Fetch Warmups
            response.Warmups = await _context.WrkWorkouts
                .Where(w => w.Category == "WARMUP" && w.IsActive == true)
                .Where(w => w.MuscleGroup == dayDef.DayType || w.MuscleGroup == "WHOLE_BODY")
                .OrderBy(r => Guid.NewGuid())
                .Take(2)
                .Select(w => new WorkoutExerciseDto
                {
                    Id = w.WorkoutId,
                    Name = w.WorkoutName,
                    ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                        ? $"{baseUrl}/images/workouts/{GetFolderByCategory(w.Category)}/{w.ImgFilename}" : "",
                    Description = w.Notes ?? "Prepare your body!",
                    RestSeconds = 30,
                    DurationMinutes = 5,
                    Calories = (int)(w.CaloriesBurned ?? 15)
                }).ToListAsync();

            // 3. Fetch Main Workouts
            int currentStep = (activeProgram.CurrentDayNo <= 14) ? 1 : 2;
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
                                           ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                                               ? $"{baseUrl}/images/workouts/{GetFolderByCategory(w.Category)}/{w.ImgFilename}" : "",
                                           Description = w.Notes ?? "Follow form.",
                                           RestSeconds = dw.RestSeconds ?? 45,
                                           DurationMinutes = Math.Max(6, (int)(((dw.SetsDefault * dw.RepsDefault * 4) + (dw.SetsDefault * (dw.RestSeconds ?? 45))) / 60)),
                                           Calories = (int)(w.CaloriesBurned ?? 50),
                                           Order = dw.WorkoutOrder
                                       }).ToListAsync();

            // --- ETO YUNG IMPORTANTE BABE: CALCULATE TOTALS ---
            response.TotalDuration = response.Warmups.Sum(x => x.DurationMinutes) +
                                     response.Workouts.Sum(x => x.DurationMinutes);

            response.TotalCalories = response.Warmups.Sum(x => x.Calories) +
                                     response.Workouts.Sum(x => x.Calories);

            return Ok(response);
        }

        [HttpGet("plan/{dayNo}")]
        public async Task<ActionResult<DailyWorkoutPlanDto>> GetWorkoutByDay(int dayNo)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null) return NotFound(new { message = "No active program." });

            var template = await _context.WrkProgramTemplates
                .FirstOrDefaultAsync(t => t.ProgramId == activeProgram.ProgramId);

            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId
                                       && d.DayNo == dayNo);

            if (dayDef == null) return NotFound(new { message = "Day definition not found." });

            var sessionLog = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == dayNo);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // Master Logic for Calendar Status
            string finalStatus;
            if (sessionLog != null)
            {
                finalStatus = sessionLog.Status; // "Completed" or "Skipped"
            }
            else if (dayNo == activeProgram.CurrentDayNo)
            {
                finalStatus = "Pending";
            }
            else if (dayNo < activeProgram.CurrentDayNo)
            {
                finalStatus = "Cancelled"; // Past day with no record
            }
            else
            {
                finalStatus = "Not Started"; // Future day
            }

            var response = new DailyWorkoutPlanDto
            {
                DayNo = dayDef.DayNo,
                DayType = dayDef.DayType,
                Message = finalStatus,
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = template?.ProgramName ?? "FlexiFit Program",
                    Description = template?.Description ?? "",
                    Level = template?.FitnessLevel ?? "",
                    Status = finalStatus,
                    Day = dayDef.DayNo
                }
            };

            if (dayDef.DayType != null && dayDef.DayType.ToUpper().Contains("REST"))
            {
                return Ok(response);
            }

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
                                           ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                                            ? $"{baseUrl}/images/workouts/{GetFolderByCategory(w.Category)}/{w.ImgFilename}" : "",
                                           Description = w.Notes ?? "No instruction available.",
                                           VideoUrl = w.VideoUrl,
                                           RestSeconds = dw.RestSeconds ?? 45,

                                           // REVISED: Mas malinis na Duration logic (min 6 mins)
                                           DurationMinutes = Math.Max(6, (int)(((dw.SetsDefault * dw.RepsDefault * 4) + (dw.SetsDefault * (dw.RestSeconds ?? 45))) / 60)),

                                           // REVISED: Base sa screenshot mo na decimal? ang CaloriesBurned
                                           Calories = (int)(w.CaloriesBurned ?? 0),

                                           Order = dw.WorkoutOrder,

                                           // DAGDAG: Para malaman ng Android kung tapos na ba 'to
                                           // DAGDAG: Para malaman ng Android kung tapos na ba 'to
                                           // Chine-check natin sa Session table (parent) ang status dahil doon nakalagay ang "COMPLETED"
                                           IsCompleted = _context.UsrUserSessionWorkouts.Any(sw =>
                                               sw.Session.InstanceId == activeProgram.InstanceId &&
                                               sw.Session.DayNo == dayDef.DayNo &&
                                               sw.WorkoutId == w.WorkoutId &&
                                               sw.Session.Status == "Completed") // Dinugtungan natin ng .Session para ma-access yung Status
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

            var newSession = new UsrUserWorkoutSession
            {
                UserId = userId.Value,
                ProgramInstanceId = activeProgram.InstanceId,
                WorkoutDay = (short)activeProgram.CurrentDayNo,
                Status = req.Status ?? "Completed",
                StartedAt = DateTime.UtcNow.AddMinutes(-req.TotalMinutes),
                CompletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            // Log activity only if not skipped
            if (newSession.Status.ToUpper() != "SKIPPED" && newSession.Status.ToUpper() != "CANCELLED")
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

            // 28-day Progression Logic
            if (activeProgram.CurrentDayNo < 28)
            {
                activeProgram.CurrentDayNo += 1;
            }
            else
            {
                await HandleLevelProgression(activeProgram);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Progress Saved!", nextDay = activeProgram.CurrentDayNo });
        }

        private string GetFolderByCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return "muscle_gain";
            return category.ToUpper() switch
            {
                "MUSCLE_GAIN" => "muscle_gain",
                "CARDIO" => "cardio",
                "REHAB" => "rehab",
                "WARMUP" => "muscle_gain",
                _ => "muscle_gain"
            };
        }

        private async Task HandleLevelProgression(UsrUserProgramInstance activeProgram)
        {
            var profile = await _context.UsrUserProfileVersions
                .FirstOrDefaultAsync(p => p.UserId == activeProgram.UserId && p.IsCurrent == true);

            if (profile == null) return;

            string currentLevel = profile.FitnessLevelSelected?.ToUpper() ?? "BEGINNER";
            string nextLevel = currentLevel switch
            {
                "BEGINNER" => "INTERMEDIATE",
                "INTERMEDIATE" => "ADVANCED",
                "ADVANCED" => "ADVANCED",
                _ => "BEGINNER"
            };

            profile.FitnessLevelSelected = nextLevel;

            var nextProgram = await _context.WrkProgramTemplates
                .Where(t => t.IsActive == true)
                .Where(t => t.FitnessLevel == nextLevel)
                .Where(t => t.ProgramCategory == profile.GoalSelected)
                .OrderBy(r => Guid.NewGuid())
                .FirstOrDefaultAsync();

            if (nextProgram != null)
            {
                activeProgram.ProgramId = nextProgram.ProgramId;
            }

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