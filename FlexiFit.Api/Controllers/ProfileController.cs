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
                // 1. Update or Create User Profile (Name, Username, Gender)
                var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (profile == null)
                {
                    profile = new UsrUserProfile
                    {
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.UsrUserProfiles.Add(profile);
                }
                profile.Name = request.Name;
                profile.Username = request.Username;
                profile.Gender = request.Gender;
                profile.UpdatedAt = DateTime.UtcNow;

                // 👇 INSERT THE USER UPDATE CODE RIGHT HERE
                var user = await _db.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
                if (user != null)
                {
                    if (!string.IsNullOrWhiteSpace(request.Name))
                        user.Name = request.Name;
                    if (!string.IsNullOrWhiteSpace(request.Username))
                        user.Username = request.Username;
                    user.UpdatedAt = DateTime.UtcNow;
                }

                // 2. Update or Create Nutrition Profile (Age, Height, Weight, Target Weight, Dietary Type, Goal)
                var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                if (nutProfile == null)
                {
                    nutProfile = new NtrUserNutritionProfile
                    {
                        UserId = userId,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.NtrUserNutritionProfiles.Add(nutProfile);
                }
                nutProfile.Age = (short)request.Age;
                nutProfile.HeightCm = (decimal)request.HeightCm;
                nutProfile.WeightKg = (decimal)request.WeightKg;
                nutProfile.TargetWeightKg = (decimal)request.TargetWeightKg;
                nutProfile.DietaryType = request.DietaryType;
                nutProfile.NutritionGoal = request.BodyCompGoal;
                nutProfile.UpdatedAt = DateTime.UtcNow;

                // 3. Update or Create User Metrics (for weight history)
                var metrics = await _db.UsrUserMetrics
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.RecordedAt)
                    .FirstOrDefaultAsync();
                if (metrics == null)
                {
                    metrics = new UsrUserMetric { UserId = userId };
                    _db.UsrUserMetrics.Add(metrics);
                }
                metrics.CurrentWeightKg = (decimal)request.WeightKg;
                metrics.CurrentHeightCm = (decimal)request.HeightCm;
                metrics.RecordedAt = DateTime.UtcNow;
                metrics.FitnessGoal = request.BodyCompGoal ?? "MAINTAIN";

                // 4. Update or Create Onboarding Details (health flags, fitness level, environment, etc.)
                var onboarding = await _db.UsrUserOnboardingDetails.FirstOrDefaultAsync(o => o.UserId == userId);
                if (onboarding == null)
                {
                    onboarding = new UsrUserOnboardingDetail { UserId = userId };
                    _db.UsrUserOnboardingDetails.Add(onboarding);
                }
                onboarding.UpperBodyInjury = request.UpperBodyInjury;
                onboarding.LowerBodyInjury = request.LowerBodyInjury;
                onboarding.JointProblems = request.JointProblems;
                onboarding.ShortBreath = request.ShortBreath;
                onboarding.HealthNone = !(request.UpperBodyInjury || request.LowerBodyInjury || request.JointProblems || request.ShortBreath);
                onboarding.ActivityLevel = request.FitnessLifestyle;
                onboarding.FitnessLevel = request.FitnessLevel;
                onboarding.Environment = request.Environment;
                onboarding.BodyGoal = request.BodyCompGoal;
                onboarding.DietType = request.DietaryType;
                onboarding.FitnessGoals = request.FitnessGoals != null ? string.Join(",", request.FitnessGoals) : "";
                onboarding.SelectedPrograms = request.SelectedPrograms != null ? string.Join(",", request.SelectedPrograms) : "";
                onboarding.UpdatedAt = DateTime.UtcNow;

                // 5. Update or Create Cycle Target (Nutrition Targets)
                var cycle = await _db.NtrUserCycleTargets
                    .OrderByDescending(c => c.CreatedAt)
                    .FirstOrDefaultAsync(c => c.UserId == userId);
                if (cycle == null)
                {
                    cycle = new NtrUserCycleTarget
                    {
                        UserId = userId,
                        StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        WeeksInCycle = 4,
                        GoalType = request.BodyCompGoal
                    };
                    _db.NtrUserCycleTargets.Add(cycle);
                }

                // Calculate BMR based on new weight, height, age, gender
                double weightKg = request.WeightKg;
                double heightCm = request.HeightCm;
                int age = request.Age;
                double bmr = (request.Gender?.ToUpper() == "MALE")
                    ? (10 * weightKg) + (6.25 * heightCm) - (5 * age) + 5
                    : (10 * weightKg) + (6.25 * heightCm) - (5 * age) - 161;

                // Get activity level from nutrition profile (or use default)
                double activityMultiplier = 1.2; // sedentary
                if (!string.IsNullOrEmpty(request.FitnessLifestyle))
                {
                    activityMultiplier = request.FitnessLifestyle.ToUpper() switch
                    {
                        "SEDENTARY" => 1.2,
                        "LIGHTLY ACTIVE" => 1.375,
                        "ACTIVE" => 1.55,
                        "VERY ACTIVE" => 1.725,
                        _ => 1.2
                    };
                }

                double tdee = bmr * activityMultiplier;
                double targetCalories = tdee;

                // Adjust based on body composition goal
                if (request.BodyCompGoal == "LOSE")
                    targetCalories -= 500;
                else if (request.BodyCompGoal == "GAIN")
                    targetCalories += 500;

                if (targetCalories < 1200) targetCalories = 1200;

                cycle.DailyTargetNetCalories = (int)Math.Round(targetCalories);
                cycle.ProteinTargetG = (decimal)Math.Round(weightKg * 1.6); // 1.6g per kg
                cycle.CarbsTargetG = (decimal)Math.Round((targetCalories * 0.5) / 4); // 50% of calories
                cycle.FatsTargetG = (decimal)Math.Round((targetCalories * 0.3) / 9); // 30% of calories
                cycle.GoalType = request.BodyCompGoal;
                cycle.CreatedAt = DateTime.UtcNow; // update timestamp

                // 6. Save all changes
                await _db.SaveChangesAsync();

                return Ok(new { message = "Profile updated successfully!", newCalories = Math.Round(targetCalories, 0) });
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
            var onboarding = await _db.UsrUserOnboardingDetails.FirstOrDefaultAsync(o => o.UserId == userId);

            if (profile == null) return NotFound("Profile not found");

            // 1. Image URL Handling
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var finalAvatarUrl = !string.IsNullOrEmpty(profile.AvatarUrl)
                ? $"{baseUrl}/{profile.AvatarUrl}"
                : null;

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

            // 3. Total Workouts across all user programs
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

            // 4. Total Program Sessions
            int totalProgramSessions = 28; // default
            if (userProgramIds.Any())
            {
                var dayCount = await _db.WrkProgramTemplateDays
                    .CountAsync(d => userProgramIds.Contains(d.ProgramId));
                if (dayCount > 0) totalProgramSessions = dayCount;
            }

            // 5. Achievement Sync
            var unlockedBadgeKeys = await _db.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.BadgeKey)
                .ToListAsync();

            // 6. BMI Computation
            double bmi = 0;
            double h = (double)(nutProfile?.HeightCm ?? 0);
            double w = (double)(nutProfile?.WeightKg ?? 0);
            if (h > 0 && w > 0)
            {
                double heightM = h / 100.0;
                bmi = w / (heightM * heightM);
            }

            // 7. Parse selected programs and fitness goals from onboarding
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

            var response = new UserProfileResponse
            {
                Name = profile.Name ?? "User",
                Username = profile.Username ?? "Username",
                AvatarUrl = finalAvatarUrl,
                GoalSubtitle = userGoal,
                Gender = profile.Gender ?? "-",
                Age = nutProfile?.Age ?? 0,
                HeightCm = h,
                WeightKg = w,
                TargetWeightKg = (double)(nutProfile?.TargetWeightKg ?? 0),
                BMI = Math.Round(bmi, 1),
                BmiCategory = CalculateBmiCategory(bmi),
                NutritionGoal = nutProfile?.NutritionGoal ?? "",
                CompletedSessions = completedCount,
                TotalSessions = completedCount, // for backward compatibility
                TotalWorkouts = totalWorkouts,
                TotalProgramSessions = totalProgramSessions,
                SelectedPrograms = selectedPrograms,
                FitnessGoals = fitnessGoals,
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

        [Authorize]
        [HttpGet("full")]
        public async Task<IActionResult> GetFullProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var user = await _db.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound("User not found.");

            var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var cycle = await _db.NtrUserCycleTargets
                                .OrderByDescending(c => c.CreatedAt)
                                .FirstOrDefaultAsync(c => c.UserId == userId);
            var onboarding = await _db.UsrUserOnboardingDetails
                                .FirstOrDefaultAsync(o => o.UserId == userId);

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var finalAvatarUrl = !string.IsNullOrEmpty(profile?.AvatarUrl)
                ? $"{baseUrl}/{profile.AvatarUrl}"
                : null;

            // Age – try from profile birth date first, else from nutProfile
            int age = 0;
            if (profile?.BirthDate.HasValue == true)
            {
                var today = DateTime.Today;
                age = today.Year - profile.BirthDate.Value.Year;
                if (new DateTime(profile.BirthDate.Value.Year, profile.BirthDate.Value.Month, profile.BirthDate.Value.Day) > today.AddYears(-age)) age--;
            }
            else if (nutProfile?.Age > 0)
            {
                age = nutProfile.Age.Value;
            }

            // BMI
            double h = (double)(nutProfile?.HeightCm ?? 0);
            double w = (double)(nutProfile?.WeightKg ?? 0);
            double bmi = 0;
            if (h > 0 && w > 0)
            {
                double hm = h / 100.0;
                bmi = w / (hm * hm);
            }

            // Parse selected programs and fitness goals
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

            // Environment
            var environment = onboarding?.Environment?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList() ?? new List<string>();

            // Health flags
            bool upperBodyInjury = onboarding?.UpperBodyInjury ?? false;
            bool lowerBodyInjury = onboarding?.LowerBodyInjury ?? false;
            bool jointProblems = onboarding?.JointProblems ?? false;
            bool shortBreath = onboarding?.ShortBreath ?? false;
            bool isRehabUser = upperBodyInjury || lowerBodyInjury || jointProblems;

            // Total workouts across all user programs
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

            // Total program sessions
            int totalProgramSessions = 28;
            if (userProgramIds.Any())
            {
                var dayCount = await _db.WrkProgramTemplateDays
                    .CountAsync(d => userProgramIds.Contains(d.ProgramId));
                if (dayCount > 0) totalProgramSessions = dayCount;
            }

            // Completed sessions
            var completedCount = await _db.UsrUserWorkoutSessions
                .CountAsync(s => s.UserId == userId);

            // Achievements
            var unlockedBadgeKeys = await _db.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId)
                .Select(a => a.BadgeKey)
                .ToListAsync();

            string nutritionGoal = onboarding?.BodyGoal ?? cycle?.GoalType ?? "MAINTAIN_WEIGHT";
            double targetWeightKg = (double)(nutProfile?.TargetWeightKg ?? 0);
            string dietaryType = nutProfile?.DietaryType ?? "BALANCED";
            string fitnessLifestyle = onboarding?.ActivityLevel ?? "ACTIVE";
            string fitnessLevel = onboarding?.FitnessLevel ?? "INTERMEDIATE";

            var response = new UserProfileResponse
            {
                // Read name and username from usr_users
                Name = user.Name ?? profile?.Name ?? "User",
                Username = user.Username ?? profile?.Username ?? "user",
                AvatarUrl = finalAvatarUrl,
                Gender = profile?.Gender ?? "-",
                Age = age,
                HeightCm = h,
                WeightKg = w,
                TargetWeightKg = targetWeightKg,
                BMI = Math.Round(bmi, 1),
                BmiCategory = CalculateBmiCategory(bmi),
                NutritionGoal = nutritionGoal,
                GoalSubtitle = cycle?.GoalType?.Replace("_", " ").ToUpper() ?? "MAINTAIN WEIGHT",
                TotalSessions = completedCount,
                TotalWorkouts = totalWorkouts,
                CompletedSessions = completedCount,
                TotalProgramSessions = totalProgramSessions,
                SelectedPrograms = selectedPrograms,
                FitnessGoals = fitnessGoals,
                DailyCalorieTarget = cycle?.DailyTargetNetCalories ?? 0,
                ProteinG = (double)(cycle?.ProteinTargetG ?? 0),
                CarbsG = (double)(cycle?.CarbsTargetG ?? 0),
                FatsG = (double)(cycle?.FatsTargetG ?? 0),

                // New fields for hydration
                FitnessLifestyle = fitnessLifestyle,
                FitnessLevel = fitnessLevel,
                Environment = environment,
                BodyCompGoal = nutritionGoal,
                DietaryType = dietaryType,
                UpperBodyInjury = upperBodyInjury,
                LowerBodyInjury = lowerBodyInjury,
                JointProblems = jointProblems,
                ShortBreath = shortBreath,
                IsRehabUser = isRehabUser
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