using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/mobile")]
    public class MobileController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;

        public MobileController(FlexiFitDbContext context)
        {
            _context = context;
        }

        #region Helpers
        private int? GetUserId()
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }

        private static string MapNutritionGoal(string bodyGoal)
        {
            return bodyGoal?.Trim().ToLower() switch
            {
                "lose_weight" => "LOSE",
                "muscle_gain" => "GAIN",
                "maintain_weight" => "MAINTAIN",
                _ => "MAINTAIN"
            };
        }

        private double CalculateBMI(decimal? weight, decimal? height)
        {
            if (weight == null || height == null || height <= 0) return 0;
            var heightInMeters = (double)height / 100;
            var bmi = (double)weight / (heightInMeters * heightInMeters);
            return Math.Round(bmi, 1);
        }
        #endregion

        [Authorize]
        [HttpGet("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            // 1. Check Profile & Program
            var profile = await _context.UsrUserProfileVersions.FirstOrDefaultAsync(p => p.UserId == userId && p.IsCurrent);
            var programInstance = await _context.UsrUserProgramInstances.FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            // 2. Check for In-Progress Workout
            var activeSession = await _context.UsrUserWorkoutSessions
                .Where(s => s.UserId == userId && s.Status == "InProgress")
                .OrderByDescending(s => s.StartedAt)
                .Select(s => new { s.SessionId, s.StartedAt })
                .FirstOrDefaultAsync();

            return Ok(new
            {
                isProfileComplete = profile != null,
                status = (profile == null) ? "NEW" : "ACTIVE",
                currentWorkoutSession = activeSession,
                hasActiveProgram = programInstance != null
            });
        }

        [Authorize]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var today = DateTime.UtcNow.Date;
            var todayDateOnly = DateOnly.FromDateTime(today); // Idagdag mo 'to babe!

            // 1. Get Core Data (Profile, Metrics, Nutrition Profile)
            var profile = await _context.UsrUserProfileVersions.FirstOrDefaultAsync(p => p.UserId == userId && p.IsCurrent);
            var metrics = await _context.UsrUserMetrics.Where(m => m.UserId == userId).OrderByDescending(m => m.RecordedAt).FirstOrDefaultAsync();
            var nutritionProfile = await _context.NtrUserNutritionProfiles.FirstOrDefaultAsync(n => n.UserId == userId);

            // 2. NUTRITION ENGINE
            // Kunin ang cycle target para sa calories
            var target = await _context.NtrUserCycleTargets
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            // BINAGO: Mas mabilis 'to dahil sa summary record (NtrDailyLogs) na tayo kukuha ng Consumed Calories
            var dailyLog = await _context.NtrDailyLogs
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

            // Imbes na mag-SumAsync sa ibang table, kunin na natin diretso sa summary record
            var consumedToday = dailyLog?.CaloriesConsumed ?? 0;

            // 3. ACTIVITY ENGINE: Calories Burned
            var burnedToday = await _context.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly) // <-- todayDateOnly dapat dito
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            // 4. WORKOUT ENGINE: Active Program Details
            var programInstance = await _context.UsrUserProgramInstances
                .Include(p => p.Program) // Navigation from UsrUserProgramInstance to WrkProgramTemplate
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            // 5. CALENDAR ENGINE
            // BINAGO: Gumamit ng .Include(c => c.Cycle) para ma-verify ang UserId mula sa parent table (Cycle)
            var calendarDay = await _context.NtrMealPlanCalendars
                .Include(c => c.Cycle)
                .FirstOrDefaultAsync(c =>
                    c.Cycle.UserId == userId &&
                    c.Status == "ACTIVE" &&
                    c.PlanDate == todayDateOnly);

            // --- RETURN OBJECT ---
            return Ok(new
            {
                fitnessLevel = profile?.FitnessLevelSelected ?? "Beginner",
                goal = profile?.GoalSelected ?? "Maintain",

                // Metrics Card
                weight = metrics?.CurrentWeightKg ?? 0,
                bmi = CalculateBMI(metrics?.CurrentWeightKg, metrics?.CurrentHeightCm),

                // Nutrition Engine Result (Circular Progress Bar data)
                nutrition = new
                {
                    targetCalories = target?.DailyTargetNetCalories ?? 0,
                    consumed = consumedToday,
                    burned = burnedToday,

                    // BINAGO: Added calculation for Net and Remaining para hindi na mag-compute ang Android side
                    netCalories = consumedToday - burnedToday,
                    remaining = (target?.DailyTargetNetCalories ?? 0) - (consumedToday - burnedToday)
                },

                // Workout Progress Card
                program = new
                {
                    name = programInstance?.Program?.ProgramName ?? "No Active Program",
                    dayNo = programInstance?.CurrentDayNo ?? 1,
                    isWorkoutDay = calendarDay?.IsWorkoutDay ?? false
                }
            });
        }

        [Authorize]
        [HttpGet("calendar-history")]
        public async Task<IActionResult> GetCalendarHistory([FromQuery] int month, [FromQuery] int year, [FromQuery] string type)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var history = new List<CalendarHistoryDto>();

            if (type == "WORKOUT")
            {
                // Kukuha tayo sa Sessions table para malaman kung anong araw may natapos na workout
                history = await _context.UsrUserWorkoutSessions
                    .Where(s => s.UserId == userId &&
                                s.StartedAt.Month == month &&
                                s.StartedAt.Year == year &&
                                s.Status == "Completed")
                    .Select(s => new CalendarHistoryDto
                    {
                        Day = s.StartedAt.Day,
                        IsCompleted = true,
                        Summary = "Workout Done"
                    })
                    .Distinct() // Para hindi mag-duplicate kung dalawa workout sa isang araw
                    .ToListAsync();
            }
            else if (type == "NUTRITION")
            {
                // Kukuha tayo sa DailyLogs summary table (yung ginamit natin sa Dashboard)
                history = await _context.NtrDailyLogs
                    .Where(l => l.UserId == userId &&
                                l.PlanDate.Month == month &&
                                l.PlanDate.Year == year &&
                                l.CaloriesConsumed > 0)
                    .Select(l => new CalendarHistoryDto
                    {
                        Day = l.PlanDate.Day,
                        IsCompleted = true,
                        Summary = $"{l.CaloriesConsumed} kcal"
                    })
                    .ToListAsync();
            }

            return Ok(history);
        }

        [Authorize]
        [HttpPost("onboarding/profile")]
        public async Task<IActionResult> SubmitOnboardingProfile([FromBody] OnboardingProfileRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Update/Create Base Profile
                var profile = await _context.UsrUserProfiles.FirstOrDefaultAsync(x => x.UserId == userId);
                if (profile == null)
                {
                    profile = new UsrUserProfile { UserId = userId.Value, CreatedAt = DateTime.UtcNow };
                    _context.UsrUserProfiles.Add(profile);
                }
                profile.Gender = request.Gender;
                profile.UpdatedAt = DateTime.UtcNow;

                // 2. Versioning & Goal
                var oldVersions = await _context.UsrUserProfileVersions.Where(x => x.UserId == userId && x.IsCurrent).ToListAsync();
                foreach (var old in oldVersions) old.IsCurrent = false;

                var profileVersion = new UsrUserProfileVersion
                {
                    UserId = userId.Value,
                    FitnessLevelSelected = request.FitnessLevel,
                    GoalSelected = request.BodyGoal ?? "Maintain",
                    IsCurrent = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.UsrUserProfileVersions.Add(profileVersion);

                // 3. Metrics & Nutrition
                var metrics = new UsrUserMetric
                {
                    UserId = userId.Value,
                    CurrentWeightKg = (decimal)request.WeightKg,
                    CurrentHeightCm = (decimal)request.HeightCm,
                    NutritionGoal = MapNutritionGoal(request.BodyGoal),
                    RecordedAt = DateTime.UtcNow
                };
                _context.UsrUserMetrics.Add(metrics);

                // Save initial progress to kick off status
                var user = await _context.UsrUsers.FindAsync(userId);
                if (user != null) user.Status = "ACTIVE";

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { message = "Profile completed", status = "ACTIVE" });
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                return StatusCode(500, "Internal Server Error during onboarding.");
            }
        }
    }
}