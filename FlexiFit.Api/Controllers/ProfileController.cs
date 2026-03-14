using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.IO;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly IConfiguration _config;

        // ideclare dito ang db
        private readonly FlexiFitDbContext _db;

        // Dito sa constructor, dapat dalawa na silang tinatanggap
        public ProfileController(IConfiguration config, FlexiFitDbContext db)
        {
            _config = config;
            _db = db;
        }

        [Authorize]
        [HttpPost("complete")]
        public async Task<IActionResult> Complete([FromBody] OnboardingProfileRequest request)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("user_id")?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            // 1. Math (Retain natin yung dati)
            double weight = (double)request.WeightKg;
            double height = (double)request.HeightCm;
            double bmr = (request.Gender?.ToUpper() == "MALE")
                ? (10 * weight) + (6.25 * height) - (5 * request.Age) + 5
                : (10 * weight) + (6.25 * height) - (5 * request.Age) - 161;

            double calorieTarget = bmr * 1.2;
            double proteinGrams = weight * 2.0;
            double carbsGrams = (calorieTarget * 0.5) / 4.0;
            double fatGrams = (calorieTarget * 0.25) / 9.0;

            try
            {
                // 2. Saving to NtrUserNutritionProfile (Age)
                var nutProfile = new NtrUserNutritionProfile
                {
                    UserId = userId,
                    Age = (short)request.Age
                };
                _db.NtrUserNutritionProfiles.Add(nutProfile);

                // 3. Saving to NtrUserCycleTarget (Macros/Calories)
                var cycleTarget = new NtrUserCycleTarget
                {
                    UserId = userId,
                    DailyTargetNetCalories = (int)calorieTarget,
                    ProteinTargetG = (decimal)proteinGrams,
                    CarbsTargetG = (decimal)carbsGrams,
                    FatsTargetG = (decimal)fatGrams,
                    GoalType = request.BodyGoal,
                    StartDate = DateOnly.FromDateTime(DateTime.Now),
                    CreatedAt = DateTime.UtcNow
                };
                _db.NtrUserCycleTargets.Add(cycleTarget);

                // 4. Update Gender sa UsrUserProfile
                var userProfile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (userProfile != null)
                {
                    userProfile.Gender = request.Gender;
                }

                // 🔥 ETO YUNG MISSING PIECE! 🔥
                // Kung wala ito, parang nagsulat ka lang sa hangin.
                await _db.SaveChangesAsync();

                return Ok(new { message = "Registration Complete!", calorieTarget = Math.Round(calorieTarget, 0) });
            }
            // ETO YUNG NAWALA: Dapat may catch partner ang try!
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [Authorize]
        [HttpPut("update-full")]
        public async Task<IActionResult> UpdateFullProfile([FromBody] UpdateOnboardingRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            try
            {
                // 1. Update Basic Profile (Gender, Goal, etc.)
                var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (profile != null)
                {
                    profile.Gender = request.Gender;
                    // Kung may column ka for lifestyle o level, i-map mo rin dito
                    profile.UpdatedAt = DateTime.UtcNow;
                }

                // 2. Update Nutrition Profile (Age, Height, Weight)
                var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (nutProfile != null)
                {
                    nutProfile.Age = (short)request.Age;
                    nutProfile.HeightCm = (decimal)request.HeightCm;
                    nutProfile.WeightKg = (decimal)request.WeightKg;
                }

                // 3. Re-calculate Macros & Update Cycle Target
                // Gamitin natin yung math logic mo sa 'Complete' endpoint
                double bmr = (request.Gender?.ToUpper() == "MALE")
                    ? (10 * request.WeightKg) + (6.25 * request.HeightCm) - (5 * request.Age) + 5
                    : (10 * request.WeightKg) + (6.25 * request.HeightCm) - (5 * request.Age) - 161;

                double calorieTarget = bmr * 1.2;

                var cycle = await _db.NtrUserCycleTargets
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(c => c.UserId == userId);

                if (cycle != null)
                {
                    cycle.DailyTargetNetCalories = (int)calorieTarget;
                    cycle.ProteinTargetG = (decimal)(request.WeightKg * 2.0);
                    cycle.GoalType = request.BodyCompGoal;
                    cycle.CreatedAt = DateTime.UtcNow;
                }

                // 4. Save everything
                await _db.SaveChangesAsync();

                return Ok(new { message = "Profile updated successfully!", newCalories = Math.Round(calorieTarget, 0) });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Update failed: {ex.Message}");
            }
        }

        [HttpPost("upload-avatar")]
        [Consumes("multipart/form-data")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadAvatar([FromForm] UploadAvatarForm form)
        {
            // 1. Kuhanin ang UserId sa Claims (The Secure Way)
            // Siguraduhin na may 'using System.Security.Claims;' sa taas ng controller mo
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("User ID not found in token.");

            int userId = int.Parse(userIdClaim);
            var file = form.File;

            // 2. Validation
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(ext))
                return BadRequest("Unsupported file type.");

            // 3. Unique FileName gamit ang extracted userId
            var fileName = $"avatar_{userId}_{Guid.NewGuid().ToString("N")}{ext}";

            // 4. Setup Directory
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var filePath = Path.Combine(uploadsDir, fileName);

            // 5. Save the file
            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // 6. Database Update (Scaffolded EF Core)
            var avatarUrl = $"uploads/avatars/{fileName}";

            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

            if (profile == null)
            {
                _db.UsrUserProfiles.Add(new UsrUserProfile
                {
                    UserId = userId,
                    AvatarUrl = avatarUrl,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                profile.AvatarUrl = avatarUrl;
                profile.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Ok(new { url = avatarUrl });
        }

        // --- 1. GET ALL PROFILE DETAILS (Para sa lahat ng Dialogs) ---
        [Authorize]
        [HttpGet("details")]
        public async Task<IActionResult> GetProfileDetails()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            // 1. Kunin ang basic profile at nutrition data
            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var cycle = await _db.NtrUserCycleTargets.OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(c => c.UserId == userId);

            // Dito natin kukunin yung Goal. Kung null, "Not Set" ang fallback.
            // Siguraduhin na ang column name sa SQL ay 'Goal' o 'FitnessGoal'
            string userGoal = cycle?.GoalType?.Replace("_", " ").ToUpper() ?? "MAINTAIN WEIGHT";

            // 2. WORKOUT LOGIC: Actual vs Target
            // A. Bilangin ang actual na nagawa na niya (Kahit anong status basta nag-record)
            var completedCount = await _db.UsrUserWorkoutSessions.CountAsync(s => s.UserId == userId);

            // B. Hanapin ang ACTIVE program instance (gamit ang 'Status == true')
            // Dahil string ang 'Status' at "ACTIVE" ang ginagamit mo, ganito dapat:
            var activeProgram = await _db.UsrUserProgramInstances
                .FirstOrDefaultAsync(up => up.UserId == userId && up.Status == "ACTIVE");

            int targetQuota = 0;
            if (activeProgram != null)
            {
                // Bilangin kung ilang rows ang naka-assign sa program na ito sa calendar template
                targetQuota = await _db.WrkProgramTemplateDays
                    .CountAsync(td => td.ProgramId == activeProgram.ProgramId);
            }

            if (profile == null) return NotFound("Profile not found");

            // --- 2.1 ACHIEVEMENT LOGIC (DAGDAG DITO) ---
            // Kunin ang listahan ng badge keys para mag-sync sa AchievementEngine.kt
            var unlockedBadgeKeys = await _db.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.BadgeKey)
                .ToListAsync();

            var achievementCount = unlockedBadgeKeys.Count;

            if (profile == null) return NotFound("Profile not found");

            // 3. BMI Computation
            double bmi = 0;
            double h = (double)(nutProfile?.HeightCm ?? 0);
            double w = (double)(nutProfile?.WeightKg ?? 0);
            if (h > 0 && w > 0)
            {
                double heightM = h / 100.0;
                bmi = w / (heightM * heightM);
            }

            // 4. Mapping to Response DTO
            var response = new UserProfileResponse
            {
                Name = profile.Name ?? "User",
                Username = profile?.Username ?? "Username",
                GoalSubtitle = userGoal, // Lalabas na dito yung "Gain Weight", etc.
                Gender = profile.Gender ?? "-",
                Age = nutProfile?.Age ?? 0,
                HeightCm = h,
                WeightKg = w,
                BMI = Math.Round(bmi, 1),
                BmiCategory = CalculateBmiCategory(bmi),

                // Eto yung para sa Workout Data Dialog mo
                CompletedSessions = completedCount,      // e.g., 5
                TotalProgramSessions = targetQuota,       // e.g., 16 or 28

                AchievementCount = achievementCount,
                UnlockedBadgeKeys = unlockedBadgeKeys,

                // Nutritional Data
                DailyCalorieTarget = cycle?.DailyTargetNetCalories ?? 0,
                ProteinG = (double)(cycle?.ProteinTargetG ?? 0),
                CarbsG = (double)(cycle?.CarbsTargetG ?? 0),
                FatsG = (double)(cycle?.FatsTargetG ?? 0)
            };

            return Ok(response);
        }

        [Authorize]
        [HttpPatch("weight")]
        public async Task<IActionResult> UpdateWeight([FromBody] UpdateWeightRequest request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (nutProfile == null) return NotFound();

            // 1. I-update ang weight
            nutProfile.WeightKg = request.NewWeight;

            // 2. I-save sa SQL
            await _db.SaveChangesAsync();

            return Ok(new { message = "Weight updated successfully!" });
        }

        private string CalculateBmiCategory(double bmi)
        {
            if (bmi < 18.5) return "Underweight";
            if (bmi < 25) return "Normal";
            if (bmi < 30) return "Overweight";
            return "Obese";
        }
    }
}