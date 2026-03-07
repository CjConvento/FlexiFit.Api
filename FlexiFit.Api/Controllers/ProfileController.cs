using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.IO;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/profile")]
    public class ProfileController : ControllerBase
    {
        private readonly IConfiguration _config;

        public ProfileController(IConfiguration config)
        {
            _config = config;
        }

        [HttpPost("complete")]
        public IActionResult Complete([FromBody] OnboardingProfileRequest request)


        {
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            // ---- Basic guards ----
            if (request.HeightCm <= 0 || request.WeightKg <= 0 || request.Age <= 0)
                return BadRequest("Invalid Age/Height/Weight.");

            // ---- BMI ----
            var heightM = request.HeightCm / 100.0;
            var bmi = request.WeightKg / (heightM * heightM);

            // ---- BMR (Mifflin-St Jeor) ----
            var gender = (request.Gender ?? "").Trim().ToLowerInvariant();
            var isMale = gender == "male" || gender == "m";
            var bmr = isMale
                ? (10 * request.WeightKg) + (6.25 * request.HeightCm) - (5 * request.Age) + 5
                : (10 * request.WeightKg) + (6.25 * request.HeightCm) - (5 * request.Age) - 161;

            // ---- Activity factor (fixed mapping) ----
            var activity = (request.ActivityLevel ?? "").Trim().ToLowerInvariant();
            var factor = activity switch
            {
                "sedentary" => 1.2,
                "light" => 1.375,
                "moderate" => 1.55,
                "active" => 1.725,
                "very_active" => 1.9,
                _ => 1.2
            };

            var tdee = bmr * factor;

            // ---- Calorie target (goal-based) ----
            var goal = (request.BodyGoal ?? "").Trim().ToLowerInvariant();
            var calorieTarget = goal switch
            {
                "lose_weight" => tdee - 500,
                "gain_weight" => tdee + 300,
                "maintain" => tdee,
                _ => tdee
            };

            // Optional safety clamp (avoid too low)
            if (calorieTarget < 1200) calorieTarget = 1200;

            // ---- Baseline macro split (goal-based) ----
            double pPct, cPct, fPct;
            switch (goal)
            {
                case "lose_weight":
                    pPct = 0.40; cPct = 0.30; fPct = 0.30;
                    break;

                case "gain_weight":
                    pPct = 0.30; cPct = 0.45; fPct = 0.25;
                    break;

                case "maintain":
                default:
                    pPct = 0.30; cPct = 0.40; fPct = 0.30;
                    break;
            }

            // ---- DietType modifier (ADDITIONAL ONLY; does not change TDEE/calorieTarget) ----
            var diet = (request.DietType ?? "").Trim().ToLowerInvariant();
            switch (diet)
            {
                case "high_protein":
                    // +10% protein, -10% carbs (fat unchanged)
                    pPct = Math.Min(0.50, pPct + 0.10);
                    cPct = Math.Max(0.10, cPct - 0.10);
                    break;

                case "keto":
                    // cap carbs at 10%, keep protein baseline, rest to fat
                    cPct = Math.Min(cPct, 0.10);
                    fPct = 1.0 - (pPct + cPct);

                    // ensure fat isn't too low; if low, reduce protein a bit
                    if (fPct < 0.20)
                    {
                        var need = 0.20 - fPct;
                        pPct = Math.Max(0.20, pPct - need);
                        fPct = 1.0 - (pPct + cPct);
                    }
                    break;

                case "vegan":
                case "vegetarian":
                    // small shift: +5% carbs, -5% fat (protein stays)
                    cPct = Math.Min(0.60, cPct + 0.05);
                    fPct = Math.Max(0.15, fPct - 0.05);
                    break;

                case "lactose":
                    // lactose-free = restriction only; no macro changes
                    break;

                case "balanced":
                default:
                    break;
            }

            // ---- Normalize macros (make sure they sum to 1.0) ----
            var total = pPct + cPct + fPct;
            if (total <= 0) { pPct = 0.30; cPct = 0.40; fPct = 0.30; total = 1.0; }
            pPct /= total; cPct /= total; fPct /= total;

            // ---- Macro grams ----
            var proteinGrams = (calorieTarget * pPct) / 4.0;
            var carbsGrams = (calorieTarget * cPct) / 4.0;
            var fatGrams = (calorieTarget * fPct) / 9.0;

            return Ok(new
            {
                bmi = Math.Round(bmi, 2),
                tdee = Math.Round(tdee, 0),
                calorieTarget = Math.Round(calorieTarget, 0),
                macroPercents = new
                {
                    protein = Math.Round(pPct * 100, 0),
                    carbs = Math.Round(cPct * 100, 0),
                    fat = Math.Round(fPct * 100, 0),
                },
                proteinGrams = Math.Round(proteinGrams, 0),
                carbsGrams = Math.Round(carbsGrams, 0),
                fatGrams = Math.Round(fatGrams, 0),
            });
        }

        [HttpPost("upload-avatar")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadAvatar([FromForm] UploadAvatarForm form)
        {
            var file = form.File;
            var userId = form.UserId;

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowed.Contains(ext))
                return BadRequest("Unsupported file type.");

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var fileName = $"user_{userId}_{DateTime.UtcNow.Ticks}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var avatarUrl = $"/uploads/avatars/{fileName}";

            var cs = _config.GetConnectionString("FlexifitDb");
            await using (var conn = new SqlConnection(cs))
            {
                await conn.OpenAsync();

                var cmd = new SqlCommand(@"
UPDATE dbo.usr_user_profiles
SET avatar_url = @url,
    updated_at = SYSUTCDATETIME()
WHERE user_id = @userId;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.usr_user_profiles (user_id, avatar_url, created_at, updated_at)
    VALUES (@userId, @url, SYSUTCDATETIME(), SYSUTCDATETIME());
END
", conn);

                cmd.Parameters.Add("@url", SqlDbType.NVarChar, 500).Value = avatarUrl;
                cmd.Parameters.Add("@userId", SqlDbType.Int).Value = userId;

                await cmd.ExecuteNonQueryAsync();
            }

            return Ok(new { url = avatarUrl });
        }
    }
}