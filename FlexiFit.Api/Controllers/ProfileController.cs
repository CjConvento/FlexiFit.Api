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

        [Authorize]
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

            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var cycle = await _db.NtrUserCycleTargets.OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(c => c.UserId == userId);

            if (profile == null) return NotFound("Profile not found");

            // 1. Image URL Handling (Para sa Glide/Coil sa Android)
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var finalAvatarUrl = !string.IsNullOrEmpty(profile.AvatarUrl)
                ? $"{baseUrl}/{profile.AvatarUrl}"
                : null; // Fallback sa Android (default icon)

            string userGoal = cycle?.GoalType?.Replace("_", " ").ToUpper() ?? "MAINTAIN WEIGHT";

            // 2. Workout Progress Logic
            var completedCount = await _db.UsrUserWorkoutSessions.CountAsync(s => s.UserId == userId);
            var activeProgram = await _db.UsrUserProgramInstances
                .FirstOrDefaultAsync(up => up.UserId == userId && up.Status == "ACTIVE");

            int targetQuota = 0;
            if (activeProgram != null)
            {
                targetQuota = await _db.WrkProgramTemplateDays
                    .CountAsync(td => td.ProgramId == activeProgram.ProgramId);
            }

            // 3. Achievement Sync
            var unlockedBadgeKeys = await _db.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.BadgeKey)
                .ToListAsync();

            // 4. BMI Computation
            double bmi = 0;
            double h = (double)(nutProfile?.HeightCm ?? 0);
            double w = (double)(nutProfile?.WeightKg ?? 0);
            if (h > 0 && w > 0)
            {
                double heightM = h / 100.0;
                bmi = w / (heightM * heightM);
            }

            var response = new UserProfileResponse
            {
                Name = profile.Name ?? "User",
                Username = profile.Username ?? "Username",
                AvatarUrl = finalAvatarUrl, // DINAGDAG: Importante ito!
                GoalSubtitle = userGoal,
                Gender = profile.Gender ?? "-",
                Age = nutProfile?.Age ?? 0,
                HeightCm = h,
                WeightKg = w,
                BMI = Math.Round(bmi, 1),
                BmiCategory = CalculateBmiCategory(bmi),
                CompletedSessions = completedCount,
                TotalProgramSessions = targetQuota,
                AchievementCount = unlockedBadgeKeys.Count,
                UnlockedBadgeKeys = unlockedBadgeKeys,
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
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var cycle = await _db.NtrUserCycleTargets.OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(c => c.UserId == userId);

            if (nutProfile == null) return NotFound("Nutrition profile not found.");

            nutProfile.WeightKg = (decimal)request.NewWeight;

            if (cycle != null)
            {
                double currentWeight = (double)request.NewWeight;
                double currentHeight = (double)nutProfile.HeightCm;
                int currentAge = (int)nutProfile.Age;

                double bmr = (profile?.Gender?.ToUpper() == "MALE")
                    ? (10 * currentWeight) + (6.25 * currentHeight) - (5 * currentAge) + 5
                    : (10 * currentWeight) + (6.25 * currentHeight) - (5 * currentAge) - 161;

                cycle.DailyTargetNetCalories = (int)(bmr * 1.2);
                cycle.ProteinTargetG = (decimal)(currentWeight * 2.0);

                // TINANGGAL NATIN ANG cycle.UpdatedAt KASI WALA ITO SA ENTITY MO
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Weight updated!" });
        }

        // --- SIGURADUHIN NA NANDITO ITONG HELPER FUNCTION SA BABA ---
        private string CalculateBmiCategory(double bmi)
        {
            if (bmi < 18.5) return "Underweight";
            if (bmi < 25) return "Normal";
            if (bmi < 30) return "Overweight";
            return "Obese";
        }

    }
}