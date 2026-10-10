using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.IO;
using System.Data;
using Npgsql;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using FlexiFit.Api.Services;

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
        private readonly NutritionCalculator _nutritionCalculator;


        // Dito sa constructor, dapat dalawa na silang tinatanggap
        public ProfileController(
            IConfiguration config, 
            FlexiFitDbContext db, 
            ILogger<ProfileController> logger,
            NutritionCalculator nutritionCalculator)
        {
            _config = config;
            _db = db;
            _logger = logger;
            _nutritionCalculator = nutritionCalculator;
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
                    profile.Name = request.Name;
                    profile.Username = request.Username;
                    profile.Gender = request.Gender;
                    profile.UpdatedAt = DateTime.UtcNow;
                }

                // 2. Update Nutrition Profile (Age, Height, Weight)
                var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (nutProfile != null)
                {
                    nutProfile.Age = (short)request.Age;
                    nutProfile.HeightCm = (decimal)request.HeightCm;
                    nutProfile.WeightKg = (decimal)request.WeightKg;
                    nutProfile.TargetWeightKg = (decimal)request.TargetWeightKg;

                    // SAVE ACTIVITY LEVEL AND DIETARY TYPE PARA HINDI MA-IGNORE
                    if (!string.IsNullOrEmpty(request.FitnessLifestyle))
                        nutProfile.ActivityLevel = request.FitnessLifestyle;
                    if (!string.IsNullOrEmpty(request.DietaryType))
                        nutProfile.DietaryType = request.DietaryType;
                    if (!string.IsNullOrEmpty(request.BodyCompGoal))
                        nutProfile.NutritionGoal = request.BodyCompGoal;
                }

                // 3. RECALCULATE CYCLE TARGETS USING CENTRALIZED CALCULATOR
                var cycle = await _db.NtrUserCycleTargets
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(c => c.UserId == userId);

                if (cycle != null)
                {
                    //  FIX: Gamitin ang centralized calculator imbes na manual BMR math
                    var targets = _nutritionCalculator.Calculate(
                        weightKg: (double)request.WeightKg,
                        heightCm: (double)request.HeightCm,
                        age: request.Age,
                        gender: request.Gender,
                        activityLevel: nutProfile?.ActivityLevel ?? "SEDENTARY", // Fallback kung wala pa
                        goal: request.BodyCompGoal ?? "MAINTAIN",
                        dietaryType: nutProfile?.DietaryType ?? "BALANCED",      // Fallback kung wala pa
                        workoutCaloriesBurned: 0 // Baseline target for profile update
                    );

                    cycle.DailyTargetNetCalories = targets.Calories;
                    cycle.ProteinTargetG = targets.ProteinG;
                    cycle.CarbsTargetG = targets.CarbsG;
                    cycle.FatsTargetG = targets.FatsG;
                    cycle.GoalType = request.BodyCompGoal;
                    cycle.CreatedAt = DateTime.UtcNow; // Mark as updated
                }

                // 4. Save everything
                await _db.SaveChangesAsync();

                // Get the calculated targets for the response (fallback to 0 if cycle is null)
                var finalCalories = cycle?.DailyTargetNetCalories ?? 0;

                return Ok(new { message = "Profile updated successfully!", newCalories = finalCalories });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update full profile failed for user {UserId}", userId);
                return StatusCode(500, $"Update failed: {ex.Message}");
            }
        }

        [Authorize]
        [HttpPost("upload-avatar")]
        [Consumes("multipart/form-data")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadAvatar(
            [FromForm] UploadAvatarForm form,
            [FromServices] IBlobService blobService)
        {
            // 1. Kuhanin ang UserId sa Claims (The Secure Way)
            // Siguraduhin na may 'using System.Security.Claims;' sa taas ng controller mo
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("User ID not found in token.");

            if (!int.TryParse(userIdClaim, out var userId))
                return Unauthorized("Invalid user ID in token.");

            var file = form.File;

            // 2. Validation
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(ext))
                return BadRequest("Unsupported file type.");

            // 3. Unique filename (used as Appwrite file name; actual fileId is generated by Appwrite)
            var fileName = $"avatar_{userId}_{Guid.NewGuid():N}{ext}";

            try
            {
                // 4. Upload to Appwrite "avatars" bucket
                using var stream = file.OpenReadStream();
                var fileId = await blobService.UploadFileAsync(stream, fileName, "avatars");

                // 5. Build full Appwrite URL — store this in DB
                var avatarUrl = blobService.GetFileUrl(fileId, "avatars");

                // 6. Upsert profile row
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

                _logger.LogInformation("Avatar uploaded to Appwrite for user {UserId}, fileId {FileId}", userId, fileId);

                return Ok(new { url = avatarUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Avatar upload failed for user {UserId}", userId);
                return StatusCode(500, new { message = "Upload failed. Please try again." });
            }
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

            //  Build avatar URL — passthrough Appwrite full URLs, prefix legacy paths
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            string? finalAvatarUrl = null;
            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                if (profile.AvatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    finalAvatarUrl = profile.AvatarUrl;                        // Appwrite URL → passthrough
                else
                    finalAvatarUrl = $"{baseUrl}/{profile.AvatarUrl.TrimStart('/')}"; // Legacy path → prefix
            }

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
    
            var user = await _db.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var cycle = await _db.NtrUserCycleTargets.OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(c => c.UserId == userId);

            if (nutProfile == null) return NotFound("Nutrition profile not found.");

            // 1. Update weight in nutrition profile
            nutProfile.WeightKg = (decimal)request.NewWeight;

            // 2. CREATE NEW METRIC ENTRY (Para may weight history graph sa future)
            // Huwag na i-update ang lumang RecordedAt para hindi masira ang history timeline
            var newMetric = new UsrUserMetric
            {
                UserId = userId,
                CurrentWeightKg = (decimal)request.NewWeight,
                CurrentHeightCm = nutProfile.HeightCm,
                FitnessGoal = nutProfile.NutritionGoal ?? "MAINTAIN",
                NutritionGoal = nutProfile.DietaryType ?? "BALANCED",
                RecordedAt = DateTime.UtcNow
            };
            _db.UsrUserMetrics.Add(newMetric);

            // 3.Recalculate cycle targets (if cycle exists)
            if (cycle != null)
            {
                double currentWeight = (double)request.NewWeight;
                double currentHeight = (double)nutProfile.HeightCm;
                int currentAge = (int)nutProfile.Age;

                // Kunin ang Gender mula sa UsrUserProfile (profile), hindi sa UsrUser
                string gender = profile?.Gender ?? "MALE"; 
                string activityLevel = nutProfile.ActivityLevel ?? "SEDENTARY";
                string goal = nutProfile.NutritionGoal?.ToUpper() ?? "MAINTAIN";
                string dietaryType = nutProfile.DietaryType ?? "BALANCED";
                
                var targets = _nutritionCalculator.Calculate(
                    weightKg: currentWeight,
                    heightCm: currentHeight,
                    age: currentAge,
                    gender: gender,
                    activityLevel: activityLevel,
                    goal: goal,
                    dietaryType: dietaryType,
                    workoutCaloriesBurned: 0 // Baseline target
                );

                cycle.DailyTargetNetCalories = targets.Calories;
                cycle.ProteinTargetG = targets.ProteinG;
                cycle.CarbsTargetG = targets.CarbsG;
                cycle.FatsTargetG = targets.FatsG;
                cycle.CreatedAt = DateTime.UtcNow; // mark as updated

                // Update new metric targets for consistency
                newMetric.CalorieTarget = targets.Calories;
                newMetric.ProteinTargetG = targets.ProteinG;
                newMetric.CarbsTargetG = targets.CarbsG;
                newMetric.FatsTargetG = targets.FatsG;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Weight updated for user {UserId} to {Weight} kg. New metric created.", userId, request.NewWeight);

            return Ok(new { message = "Weight and nutrition targets updated successfully!" });
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

            //  1. Avatar URL — passthrough Appwrite full URLs, prefix legacy paths
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            string? finalAvatarUrl = null;
            if (!string.IsNullOrEmpty(profile.AvatarUrl))
            {
                if (profile.AvatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    finalAvatarUrl = profile.AvatarUrl;                             // Appwrite → passthrough
                else
                    finalAvatarUrl = $"{baseUrl}/{profile.AvatarUrl.TrimStart('/')}"; // Legacy → prefix
            }

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