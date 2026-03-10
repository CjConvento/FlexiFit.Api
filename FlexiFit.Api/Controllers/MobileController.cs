using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        private string? GetFirebaseUid()
        {
            return User.FindFirst("firebase_uid")?.Value;
        }

        private int? GetUserId()
        {
            var raw = User.FindFirst("user_id")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }

        private static string MapNutritionGoal(string bodyGoal)
        {
            return bodyGoal.Trim().ToLower() switch
            {
                "lose_weight" => "LOSE",
                "muscle_gain" => "GAIN",
                "maintain_weight" => "MAINTAIN",
                _ => "MAINTAIN"
            };
        }

        [Authorize]
        [HttpPost("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var firebaseUid = GetFirebaseUid();
            if (string.IsNullOrWhiteSpace(firebaseUid))
            {
                return Unauthorized(new { message = "invalid token: no firebase_uid claim" });
            }

            var user = await _context.UsrUsers
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid);

            if (user == null)
            {
                return Unauthorized(new { message = "user not found" });
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var nutritionProfile = await _context.NtrUserNutritionProfiles
                .FirstOrDefaultAsync(x => x.UserId == user.UserId);

            var profileComplete = nutritionProfile != null && nutritionProfile.IsProfileComplete;

            return Ok(new
            {
                profileComplete,
                userId = user.UserId
            });
        }

        [Authorize]
        [HttpPost("onboarding/profile")]
        public async Task<IActionResult> SubmitOnboardingProfile([FromBody] OnboardingProfileRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            if (request.SelectedPrograms.Count > 4)
            {
                return BadRequest(new
                {
                    message = "You can select up to 4 programs only."
                });
            }

            var userId = GetUserId();
            if (userId == null)
            {
                return Unauthorized(new { message = "invalid token: no user_id claim" });
            }

            var user = await _context.UsrUsers
                .FirstOrDefaultAsync(u => u.UserId == userId.Value);

            if (user == null)
            {
                return Unauthorized(new { message = "user not found" });
            }

            using var tx = await _context.Database.BeginTransactionAsync();

            // 1) usr_user_profiles
            var profile = await _context.UsrUserProfiles
                .FirstOrDefaultAsync(x => x.UserId == userId.Value);

            if (profile == null)
            {
                profile = new UsrUserProfile
                {
                    UserId = userId.Value,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.UsrUserProfiles.Add(profile);
            }

            profile.Gender = request.Gender;
            profile.UpdatedAt = DateTime.UtcNow;

            // 2) usr_user_profile_versions
            var oldVersions = await _context.UsrUserProfileVersions
                .Where(x => x.UserId == userId.Value && x.IsCurrent)
                .ToListAsync();

            foreach (var old in oldVersions)
            {
                old.IsCurrent = false;
            }

            var primaryGoal = request.FitnessGoals.FirstOrDefault()
                              ?? request.BodyGoal
                              ?? "maintain_weight";

            var profileVersion = new UsrUserProfileVersion
            {
                UserId = userId.Value,
                FitnessLevelSelected = request.FitnessLevel,
                GoalSelected = primaryGoal,
                CreatedAt = DateTime.UtcNow,
                IsCurrent = true
            };

            _context.UsrUserProfileVersions.Add(profileVersion);
            await _context.SaveChangesAsync();

            // 3) usr_user_metrics
            var metrics = new UsrUserMetric
            {
                UserId = userId.Value,
                CurrentWeightKg = (decimal)request.WeightKg,
                CurrentHeightCm = (decimal)request.HeightCm,
                FitnessGoal = string.Join(",", request.FitnessGoals),
                NutritionGoal = MapNutritionGoal(request.BodyGoal),
                RecordedAt = DateTime.UtcNow
            };
            _context.UsrUserMetrics.Add(metrics);

            // 4) ntr_user_nutrition_profile
            var nutrition = await _context.NtrUserNutritionProfiles
                .FirstOrDefaultAsync(x => x.UserId == userId.Value);

            if (nutrition == null)
            {
                nutrition = new NtrUserNutritionProfile
                {
                    UserId = userId.Value
                };
                _context.NtrUserNutritionProfiles.Add(nutrition);
            }

            nutrition.Age = request.Age;
            nutrition.WeightKg = (decimal)request.WeightKg;
            nutrition.HeightCm = (decimal)request.HeightCm;
            nutrition.TargetWeightKg = (decimal)request.TargetWeightKg;
            nutrition.NutritionGoal = MapNutritionGoal(request.BodyGoal);
            nutrition.ActivityLevel = request.ActivityLevel;
            nutrition.DietaryType = request.DietType;
            nutrition.UpdatedAt = DateTime.UtcNow;
            nutrition.IsProfileComplete = true;

            // 5) usr_user_program_instances
            var selectedTemplates = await _context.WrkProgramTemplates
                .Where(p => request.SelectedPrograms.Contains(p.ProgramName))
                .ToListAsync();

            foreach (var template in selectedTemplates)
            {
                var exists = await _context.UsrUserProgramInstances.AnyAsync(x =>
                    x.UserId == userId.Value &&
                    x.ProgramId == template.ProgramId &&
                    x.ProfileVersionId == profileVersion.ProfileVersionId &&
                    x.CycleNo == 1);

                if (!exists)
                {
                    _context.UsrUserProgramInstances.Add(new UsrUserProgramInstance
                    {
                        UserId = userId.Value,
                        ProgramId = template.ProgramId,
                        ProfileVersionId = profileVersion.ProfileVersionId,
                        CycleNo = 1,
                        Status = "Active",
                        FitnessLevelAtStart = request.FitnessLevel,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                message = "Profile submitted successfully",
                profileComplete = true,
                userId = user.UserId
            });
        }
    }
}