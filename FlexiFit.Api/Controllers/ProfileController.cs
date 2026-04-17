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

        private readonly ILogger<ProfileController> _logger;

        // Dito sa constructor, dapat dalawa na silang tinatanggap
        public ProfileController(IConfiguration config, FlexiFitDbContext db, ILogger<ProfileController> logger)
        {
            _config = config;
            _db = db;
            _logger = logger;
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


        [HttpGet("details")]
        public async Task<IActionResult> GetProfileDetails()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile == null) return NotFound("Profile not found");

            // Latest metrics for current height/weight
            var latestMetric = await _db.UsrUserMetrics
                .OrderByDescending(m => m.RecordedAt)
                .FirstOrDefaultAsync(m => m.UserId == userId);

            // Nutrition profile for target weight (and possibly other data)
            var nutProfile = await _db.NtrUserNutritionProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId);

            var cycle = await _db.NtrUserCycleTargets
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            var onboarding = await _db.UsrUserOnboardingDetails
                .FirstOrDefaultAsync(o => o.UserId == userId);

            // Compute age from birth date
            int age = 0;
            if (profile.BirthDate.HasValue)
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                age = today.Year - profile.BirthDate.Value.Year;
                if (profile.BirthDate.Value > today.AddYears(-age)) age--;
            }

            // Build avatar URL
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var finalAvatarUrl = !string.IsNullOrEmpty(profile.AvatarUrl)
                ? $"{baseUrl}/{profile.AvatarUrl}"
                : null;

            string userGoal = cycle?.GoalType?.Replace("_", " ").ToUpper() ?? "MAINTAIN WEIGHT";

            var activeProgram = await _db.UsrUserProgramInstances
                .FirstOrDefaultAsync(up => up.UserId == userId && up.Status == "ACTIVE");
            int totalProgramSessions = 0;
            if (activeProgram != null)
            {
                totalProgramSessions = await _db.WrkProgramTemplateDays
                    .CountAsync(td => td.ProgramId == activeProgram.ProgramId);
            }

            var completedSessions = await _db.UsrUserWorkoutSessions
                .CountAsync(s => s.UserId == userId && s.Status == "Completed");

            var totalWorkouts = await _db.UsrUserSessionWorkouts
                .Where(sw => sw.Session.UserId == userId)
                .CountAsync();

            var selectedPrograms = onboarding?.SelectedPrograms?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToList() ?? new List<string>();

            var fitnessGoals = onboarding?.FitnessGoals?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim())
                .ToList() ?? new List<string>();

            double heightCm = (double)(latestMetric?.CurrentHeightCm ?? 0);
            double weightKg = (double)(latestMetric?.CurrentWeightKg ?? 0);
            double targetWeightKg = (double)(nutProfile?.TargetWeightKg ?? 0);

            double bmi = 0;
            if (heightCm > 0 && weightKg > 0)
            {
                double heightM = heightCm / 100.0;
                bmi = weightKg / (heightM * heightM);
            }

            string nutritionGoal = onboarding?.BodyGoal?.Replace("_", " ").ToUpper() ?? userGoal;

            var unlockedBadgeKeys = await _db.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.BadgeKey)
                .ToListAsync();

            var response = new UserProfileResponse
            {
                Name = profile.Name ?? "User",
                Username = profile.Username ?? "Username",
                AvatarUrl = finalAvatarUrl,
                GoalSubtitle = userGoal,
                Gender = profile.Gender ?? "-",
                Age = age,
                HeightCm = heightCm,
                WeightKg = weightKg,
                TargetWeightKg = targetWeightKg,
                BMI = Math.Round(bmi, 1),
                BmiCategory = CalculateBmiCategory(bmi),
                NutritionGoal = nutritionGoal,
                TotalSessions = totalProgramSessions,
                TotalWorkouts = totalWorkouts,
                CompletedSessions = completedSessions,
                TotalProgramSessions = totalProgramSessions,
                SelectedPrograms = selectedPrograms,
                FitnessGoals = fitnessGoals,
                DailyCalorieTarget = cycle?.DailyTargetNetCalories ?? 0,
                ProteinG = (double)(cycle?.ProteinTargetG ?? 0),
                CarbsG = (double)(cycle?.CarbsTargetG ?? 0),
                FatsG = (double)(cycle?.FatsTargetG ?? 0),
                AchievementCount = unlockedBadgeKeys.Count,
                UnlockedBadges = unlockedBadgeKeys,
                UnlockedBadgeKeys = unlockedBadgeKeys
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
            _db.Entry(nutProfile).State = EntityState.Modified;

            // Get the latest metric record
            var latestMetric = await _db.UsrUserMetrics
                .Where(m => m.UserId == userId)
                .OrderByDescending(m => m.RecordedAt)
                .FirstOrDefaultAsync();

            // Update weight in the latest metric (or create a new one)
            if (latestMetric != null)
            {
                latestMetric.CurrentWeightKg = (decimal)request.NewWeight;
                latestMetric.RecordedAt = DateTime.UtcNow;
            }
            else
            {
                // Use ternary operator to avoid null‑propagation casting issues
                latestMetric = new UsrUserMetric
                {
                    UserId = userId,
                    CurrentWeightKg = (decimal)request.NewWeight,
                    CurrentHeightCm = nutProfile.HeightCm,
                    FitnessGoal = nutProfile.NutritionGoal ?? "MAINTAIN",
                    NutritionGoal = nutProfile.DietaryType ?? "BALANCED",
                    CalorieTarget = cycle?.DailyTargetNetCalories ?? 2000,
                    ProteinTargetG = cycle != null ? (int)cycle.ProteinTargetG : 120,
                    CarbsTargetG = cycle != null ? (int)cycle.CarbsTargetG : 200,
                    FatsTargetG = cycle != null ? (int)cycle.FatsTargetG : 60,
                    RecordedAt = DateTime.UtcNow
                };
                _db.UsrUserMetrics.Add(latestMetric);
            }

            // Update nutrition profile weight
            nutProfile.WeightKg = (decimal)request.NewWeight;

            // Recalculate cycle targets (if cycle exists)
            if (cycle != null)
            {
                double currentWeight = (double)request.NewWeight;
                double currentHeight = (double)nutProfile.HeightCm;
                int currentAge = (int)nutProfile.Age;

                double bmr = (profile?.Gender?.ToUpper() == "MALE")
                    ? (10 * currentWeight) + (6.25 * currentHeight) - (5 * currentAge) + 5
                    : (10 * currentWeight) + (6.25 * currentHeight) - (5 * currentAge) - 161;

                string activityLevel = nutProfile.ActivityLevel ?? "SEDENTARY";
                string normalized = activityLevel.ToUpper().Replace("_", "").Replace(" ", "");
                double multiplier = normalized switch
                {
                    "SEDENTARY" => 1.2,
                    "LIGHTLYACTIVE" => 1.375,
                    "ACTIVE" => 1.55,
                    "VERYACTIVE" => 1.725,
                    _ => 1.375
                };

                double maintenanceCalories = bmr * multiplier;
                double targetWeight = nutProfile.TargetWeightKg.HasValue ? (double)nutProfile.TargetWeightKg.Value : currentWeight;
                string goal = nutProfile.NutritionGoal?.ToUpper() ?? "MAINTAIN";

                double calorieAdjustment = 0;
                if (goal == "LOSE")
                {
                    double deficit = Math.Min(0.2 * maintenanceCalories, 500);
                    calorieAdjustment = -deficit;
                }
                else if (goal == "GAIN")
                {
                    double surplus = Math.Min(0.2 * maintenanceCalories, 500);
                    calorieAdjustment = +surplus;
                }

                double weightDiff = currentWeight - targetWeight;
                if (goal == "LOSE" && weightDiff > 5) calorieAdjustment -= 100;
                else if (goal == "GAIN" && weightDiff < -5) calorieAdjustment += 100;

                int dailyCalories = (int)(maintenanceCalories + calorieAdjustment);
                dailyCalories = Math.Max(dailyCalories, 1200);

                // Recalculate all macros as integers
                int proteinTarget = (int)(currentWeight * 2.0);
                int fatsTarget = (int)((dailyCalories * 0.25) / 9);
                int carbsTarget = (int)((dailyCalories - (proteinTarget * 4) - (fatsTarget * 9)) / 4);

                cycle.DailyTargetNetCalories = dailyCalories;
                cycle.ProteinTargetG = proteinTarget;
                cycle.CarbsTargetG = carbsTarget;
                cycle.FatsTargetG = fatsTarget;
                cycle.CreatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Weight updated for user {UserId} to {Weight} kg (both metric and nutrition profile)", userId, request.NewWeight);

            return Ok(new { message = "Weight updated successfully!" });
        }

        [Authorize]
        [HttpGet("full")]
        public async Task<IActionResult> GetFullProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var cycle = await _db.NtrUserCycleTargets
                                .OrderByDescending(c => c.CreatedAt)
                                .FirstOrDefaultAsync(c => c.UserId == userId);
            var onboarding = await _db.UsrUserOnboardingDetails
                                .FirstOrDefaultAsync(o => o.UserId == userId);

            if (profile == null) return NotFound("Profile not found.");

            // 1. Avatar URL
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var finalAvatarUrl = !string.IsNullOrEmpty(profile.AvatarUrl)
                ? $"{baseUrl}/{profile.AvatarUrl}"
                : null;

            // 2. Age mula sa BirthDate
            int age = 0;
            if (profile.BirthDate.HasValue)
            {
                var today = DateTime.Today;
                age = today.Year - profile.BirthDate.Value.Year;
                    if (new DateTime(profile.BirthDate.Value.Year, profile.BirthDate.Value.Month, profile.BirthDate.Value.Day) > today.AddYears(-age)) age--;
            }

            // 3. BMI computation
            double h = (double)(nutProfile?.HeightCm ?? 0);
            double w = (double)(nutProfile?.WeightKg ?? 0);
            double bmi = 0;
            if (h > 0 && w > 0)
            {
                double hm = h / 100.0;
                bmi = w / (hm * hm);
            }

            // 4. Selected Programs at Fitness Goals mula sa onboarding
            var selectedPrograms = onboarding?.SelectedPrograms?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();

            var fitnessGoals = onboarding?.FitnessGoals?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();

            // 5. Total Workouts — count ng workouts sa lahat ng programs ng user
            var userProgramIds = await _db.UsrUserProgramInstances
                .Where(up => up.UserId == userId)
                .Select(up => up.ProgramId)
                .Distinct()
                .ToListAsync();

            int totalWorkouts = 0;
            if (userProgramIds.Any())
            {
                totalWorkouts = await _db.WrkProgramTemplateDaytypeWorkouts
                    .CountAsync(w2 => userProgramIds.Contains(w2.ProgramId));
            }

            // 6. Total Program Sessions
            int totalProgramSessions = 28; // default
            if (userProgramIds.Any())
            {
                var dayCount = await _db.WrkProgramTemplateDays
                    .CountAsync(d => userProgramIds.Contains(d.ProgramId));
                if (dayCount > 0) totalProgramSessions = dayCount;
            }

            // 7. Completed Sessions
            var completedCount = await _db.UsrUserWorkoutSessions
                .CountAsync(s => s.UserId == userId);

            // 8. Achievements
            var unlockedBadgeKeys = await _db.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.BadgeKey)
                .ToListAsync();

            // 9. Nutrition Goal at Target Weight
            string nutritionGoal = onboarding?.BodyGoal ?? cycle?.GoalType ?? "MAINTAIN_WEIGHT";
            double targetWeightKg = (double)(nutProfile?.TargetWeightKg ?? 0);

            var response = new UserProfileResponse
            {
                Name = profile.Name ?? "User",
                Username = profile.Username ?? "user",
                AvatarUrl = finalAvatarUrl,
                Gender = profile.Gender ?? "-",
                Age = age,
                HeightCm = h,
                WeightKg = w,
                TargetWeightKg = targetWeightKg,
                BMI = Math.Round(bmi, 1),
                BmiCategory = CalculateBmiCategory(bmi),
                NutritionGoal = nutritionGoal,
                GoalSubtitle = cycle?.GoalType?.Replace("_", " ").ToUpper() ?? "MAINTAIN WEIGHT",
                TotalWorkouts = totalWorkouts,
                TotalSessions = completedCount,
                CompletedSessions = completedCount,
                TotalProgramSessions = totalProgramSessions,
                SelectedPrograms = selectedPrograms,
                FitnessGoals = fitnessGoals,
                DailyCalorieTarget = cycle?.DailyTargetNetCalories ?? 0,
                ProteinG = (double)(cycle?.ProteinTargetG ?? 0),
                CarbsG = (double)(cycle?.CarbsTargetG ?? 0),
                FatsG = (double)(cycle?.FatsTargetG ?? 0),
                AchievementCount = unlockedBadgeKeys.Count,
                UnlockedBadgeKeys = unlockedBadgeKeys,
                UnlockedBadges = unlockedBadgeKeys
            };

            return Ok(response);
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