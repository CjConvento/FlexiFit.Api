using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static System.Collections.Specialized.BitVector32;

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

            // 1. Active Program Instance
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null) return NotFound(new { message = "No active program." });

            // 2. Program Template (Dito natin kukunin ang Description, babe)
            var template = await _context.WrkProgramTemplates
                .FirstOrDefaultAsync(t => t.ProgramId == activeProgram.ProgramId);

            // 3. Current Session
            var session = await _context.UsrUserWorkoutSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                                       && s.ProgramInstanceId == activeProgram.InstanceId
                                       && s.WorkoutDay == (short)activeProgram.CurrentDayNo);

            if (session == null) return NotFound(new { message = "Session not found." });

            bool isCompletedBySession = session.Status?.ToUpper() == "COMPLETED";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // 1. CALCULATE WEEK AND MONTH (Para hindi 0)
            int currentDay = session.WorkoutDay;
            int weekNo = ((currentDay - 1) / 7) + 1;
            int monthNo = ((currentDay - 1) / 28) + 1;

            // 2. Response Mapping
            var response = new DailyWorkoutPlanDto
            {
                DayNo = session.WorkoutDay,
                Status = session.Status,
                Level = template?.FitnessLevel,
                Message = isCompletedBySession ? "Mission Accomplished! 🎉" : "Time to grind, babe!",
                Program = new WorkoutProgramDto
                {
                    ProgramId = activeProgram.ProgramId,
                    ProgramName = template?.ProgramName ?? "My Routine",
                    Description = template?.Description ?? "", // FIXED: Hila na si Description
                    Day = session.WorkoutDay,
                    Week = weekNo,   // FIXED: May laman na 'to
                    Month = monthNo  // FIXED: May laman na 'to
                }
            };



            // 5. WARMUPS (Consistent Selection based on Day)
            // Kunin muna lahat ng active warmups
            var allWarmups = await _context.WrkWorkouts
                .Where(w => w.Category.ToUpper() == "WARMUP" && w.IsActive == true)
                .ToListAsync();

            if (allWarmups.Any())
            {
                // Para maging "Consistent" at hindi mag-iba-iba sa refresh:
                // Ginagamit natin yung DayNo bilang seed para sa "Random" pick.
                var seed = activeProgram.ProgramId + session.WorkoutDay;
                var random = new Random(seed);

                response.Warmups = allWarmups
                    .OrderBy(x => random.Next()) // Random pero fixed per day
                    .Take(2)
                    .Select((w, index) => new WorkoutExerciseDto
                    {
                        Id = w.WorkoutId,
                        Name = w.WorkoutName,
                        IsCompleted = isCompletedBySession,

                        // Siguraduhin ang image path, babe. 
                        // Kung may "warmups" folder ka talaga, tama 'to.
                        ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                            ? $"{baseUrl}/images/workouts/warmups/{w.ImgFilename}" : "",

                        // Defaults para sa Warmups (Para hindi puro null/0 sa JSON)
                        Sets = 2,
                        Reps = 15,
                        DurationMinutes = 5, // Default 5 mins for warmup
                        RestSeconds = 30,
                        // FIXED: Magiging 1 and 2 na ang order nito
                        Order = index + 1,
                        MuscleGroup = "Warmup",
                        Calories = 20, // Estimated burned
                        Description = w.Notes ?? "Prepare your joints and muscles for the workout.",
                        VideoUrl = w.VideoUrl
                    }).ToList();
            }


            // 3. Pagdating sa Workouts (Revised & Simplified)
            response.Workouts = await (from sw in _context.UsrUserSessionWorkouts
                                       join w in _context.WrkWorkouts on sw.WorkoutId equals w.WorkoutId
                                       where sw.SessionId == session.SessionId
                                       orderby sw.OrderNo
                                       select new WorkoutExerciseDto
                                       {
                                           Id = w.WorkoutId,
                                           Name = w.WorkoutName,
                                           Sets = sw.Sets > 0 ? sw.Sets : 3,
                                           Reps = sw.Reps > 0 ? sw.Reps : 10,

                                           // FIXED: Mas simpleng Image URL Construction
                                           ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                                               ? $"{baseUrl}/images/workouts/{w.Category.ToLower()}/{w.ImgFilename}" : "",

                                           Order = sw.OrderNo,
                                           MuscleGroup = w.Category ?? "Muscle Gain",
                                           Calories = (int)(w.CaloriesBurned ?? 50),
                                           DurationMinutes = 6,

                                           // 💡 Huwag na mag-join sa Template table para sa RestSeconds. 
                                           // Mag-hardcode o kumuha sa Workouts table kung meron.
                                           RestSeconds = 60,
                                           Description = w.Notes ?? "Perform the exercise with controlled motion.",
                                           IsCompleted = isCompletedBySession,
                                       })
                                       .ToListAsync();

            // 6. Totals
            // 2. I-calculate ang Total Calories (Warmups + Workouts)
            response.TotalCalories = (response.Warmups?.Sum(x => x.Calories) ?? 0) +
                                     (response.Workouts?.Sum(x => x.Calories) ?? 0);

            // 3. I-calculate ang Total Duration (Warmups + Workouts + transition time)
            response.TotalDuration = (response.Warmups?.Sum(x => x.DurationMinutes) ?? 0) +
                                     (response.Workouts?.Sum(x => x.DurationMinutes) ?? 0);
            return Ok(response);
        }

        private static string GetFolderByCategory(string category)
{
        if (string.IsNullOrEmpty(category)) return "others";

        // Gawin nating ToUpper para hindi tayo magkamali sa spelling
        return category.ToUpper() switch
        {
        "CARDIO" => "cardio",
        "MUSCLE_GAIN" => "muscle_gain",
        "REHAB" => "rehab",
        "WARMUP" => "cardio", // 💡 Tip: Kung wala kang warmup folder, ituro mo muna sa cardio
        _ => "others"
        };
}

        [HttpPost("complete")]
        public async Task<IActionResult> CompleteSession([FromBody] WorkoutSessionCompleteDto req)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            // 1. Precise Data Handling (Server UTC to DateOnly)
            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            // 2. Hanapin ang Active Program Instance
            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            if (activeProgram == null) return BadRequest("No active program found.");

            // 1. Siguraduhin na may default value ang minutes para hindi mag-zero out ang date
            int minutesSpent = req.TotalMinutes > 0 ? req.TotalMinutes : 30; // Default to 30 mins kung 0 ang pinasa

            var now = DateTime.UtcNow;

            var newSession = new UsrUserWorkoutSession
            {
                UserId = userId.Value,
                ProgramInstanceId = activeProgram.InstanceId,
                WorkoutDay = (short)activeProgram.CurrentDayNo,
                Status = req.Status ?? "Completed",

                // 💡 FIXED: Siguraduhin na valid ang subtraction
                StartedAt = now.AddMinutes(-minutesSpent),
                CompletedAt = now,
                CreatedAt = now
            };

            // 4. Activity Summary Log (UPSERT LOGIC)
            if (newSession.Status.ToUpper() != "SKIPPED" && newSession.Status.ToUpper() != "CANCELLED")
            {
                // Hanapin muna kung may record na si user for today
                var existingActivity = await _context.ActActivitySummaries
                    .FirstOrDefaultAsync(a => a.UserId == userId.Value && a.LogDate == todayDateOnly);

                if (existingActivity != null)
                {
                    // Kung meron na, i-update lang natin (i-add ang bagong stats)
                    existingActivity.CaloriesBurned += (short)req.TotalCalories;
                    existingActivity.TotalMinutes += (short)req.TotalMinutes;
                }
                else
                {
                    // Kung wala pa, saka lang tayo mag-a-ADD ng bago
                    _context.ActActivitySummaries.Add(new ActActivitySummary
                    {
                        UserId = userId.Value,
                        CaloriesBurned = (short)req.TotalCalories,
                        TotalMinutes = (short)req.TotalMinutes,
                        LogDate = todayDateOnly
                    });
                }
            }

            _context.UsrUserWorkoutSessions.Add(newSession);

            // 5. 💡 SIMPLE DAY PROGRESSION: Walang auto-leveling, pure day increment lang.
            // Dito natin sinisigurado na lilipat siya sa Day 2, Day 3, etc.
            if (activeProgram.CurrentDayNo < 28)
            {
                activeProgram.CurrentDayNo += 1;
            }
            else
            {
                // Kung Day 28 na, mark as COMPLETED na ang program para sa Manager
                activeProgram.Status = "COMPLETED";
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Progress Saved!",
                currentDay = activeProgram.CurrentDayNo,
                isProgramFinished = activeProgram.Status == "COMPLETED"
            });
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