using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

            // 1. Get Active Program & Template
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "Active");

            if (activeProgram == null) return NotFound(new { message = "No active program found." });

            var template = await _context.WrkProgramTemplates
                .FirstOrDefaultAsync(t => t.ProgramId == activeProgram.ProgramId);

            // 2. Get Day Definition
            var dayDef = await _context.WrkProgramTemplateDays
                .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId
                                       && d.DayNo == activeProgram.CurrentDayNo);

            if (dayDef == null) return NotFound(new { message = "Workout day not found." });

            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            var response = new DailyWorkoutPlanDto
            {
                DayNo = dayDef.DayNo,
                DayType = dayDef.DayType,
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = template?.ProgramName ?? "FlexiFit Program",
                    Environment = template?.Environment ?? "",
                    Level = template?.FitnessLevel ?? "",
                    Description = template?.Description ?? "",
                    Status = activeProgram.Status,
                    Month = dayDef.MonthNo,
                    Week = dayDef.WeekNo,
                    Day = dayDef.DayNo
                }
            };

            if (dayDef.DayType.Contains("REST"))
            {
                response.Message = "Recovery day! Let your muscles heal.";
                return Ok(response);
            }

            // 3. Smart Filtering Keyword for Warmup
            string categoryKeyword = template?.ProgramCategory.ToUpper() switch
            {
                "MUSCLE_GAIN" => "Muscle Gain",
                "CARDIO" => "Cardio",
                "REHAB" => "REHAB",
                _ => ""
            };

            // 4. Fetch Warmups (Randomized & Categorized)
            response.Warmups = await _context.WrkWorkouts
                .Where(w => w.Category == "WARMUP" && w.IsActive == 1)
                .Where(w => (w.MuscleGroup == dayDef.DayType || w.MuscleGroup == "WHOLE_BODY") &&
                            (string.IsNullOrEmpty(categoryKeyword) || w.Notes.Contains(categoryKeyword)))
                .OrderBy(r => Guid.NewGuid()).Take(2)
                .Select(w => new WorkoutExerciseDto
                {
                    Id = w.WorkoutId,
                    Name = w.WorkoutName,
                    ImageFileName = !string.IsNullOrEmpty(w.ImgFilename) ? $"{baseUrl}/uploads/images/{w.ImgFilename}" : "",
                    VideoUrl = w.VideoUrl, // Direct link to video tutorial
                    Description = w.Notes ?? "No description available.",
                    Sets = 1,
                    Reps = 15,
                    RestSeconds = 30,
                    DurationMinutes = w.Duration ?? 5,
                    Calories = w.CaloriesBurned,
                    MuscleGroup = w.MuscleGroup
                }).ToListAsync();

            // 5. Fetch Main Workouts with Load Steps
            int currentStep = (template?.ProgramCategory.ToUpper() == "REHAB") ? 1 : (dayDef.DayNo <= 14 ? 1 : 2);

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
                                           MuscleGroup = w.MuscleGroup,
                                           ImageFileName = !string.IsNullOrEmpty(w.ImgFilename) ? $"{baseUrl}/uploads/images/{w.ImgFilename}" : "",
                                           VideoUrl = w.VideoUrl,
                                           Sets = dw.SetsDefault,
                                           Reps = dw.RepsDefault,
                                           RestSeconds = dw.RestSeconds ?? 60,
                                           DurationMinutes = w.Duration ?? 10,
                                           Calories = w.CaloriesBurned,
                                           Description = w.Notes ?? ""
                                       }).ToListAsync();

            return Ok(response);
        }

        [HttpPost("complete")]
        public async Task<IActionResult> CompleteSession([FromBody] WorkoutSessionCompleteDto req)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "Active");

            if (activeProgram == null) return BadRequest("No active program.");

            // I-save ang Activity Summary
            _context.ActActivitySummary.Add(new ActActivitySummary
            {
                UserId = userId.Value,
                CaloriesBurned = req.TotalCalories,
                TotalMinutes = req.TotalMinutes,
                LogDate = DateTime.UtcNow.Date
            });

            // Increment Day Logic
            bool finished = false;
            if (activeProgram.CurrentDayNo < 28)
            {
                activeProgram.CurrentDayNo += 1;
            }
            else
            {
                activeProgram.Status = "Completed";
                finished = true;
            }

            activeProgram.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Session completed and progress saved!", isProgramFinished = finished });
        }

        private int? GetUserId()
        {
            var raw = User.FindFirst("user_id")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}