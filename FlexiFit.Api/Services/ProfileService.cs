using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class ProfileService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(FlexiFitDbContext db, ILogger<ProfileService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Unified method para sa GetFullProfile at GetProfileDetails
    /// </summary>
    public async Task<UserProfileResponse> GetUserProfileAsync(int userId, string baseUrl)
    {
        // 1. Fetch core data
        var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile == null) 
        {
            throw new Exception("Profile not found");
        }

        // Nutrition profile for target weight (and other nutrition-related data)
        var nutProfile = await _db.NtrUserNutritionProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);
            
        var cycle = await _db.NtrUserCycleTargets
                        .Where(c => c.UserId == userId)
                        .OrderByDescending(c => c.CreatedAt)
                        .FirstOrDefaultAsync(c => c.UserId == userId);

        var onboarding = await _db.UsrUserOnboardingDetails
            .FirstOrDefaultAsync(o => o.UserId == userId);

        // Kunin ang LATEST weight/height mula sa Metrics, hindi sa static NutProfile
        var latestMetric = await _db.UsrUserMetrics
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.RecordedAt)
            .FirstOrDefaultAsync();

        // I-resolve muna ang nulls gamit ang '0m' (decimal zero), THEN i-cast to double
        decimal resolvedWeight = latestMetric?.CurrentWeightKg ?? nutProfile?.WeightKg ?? 0m;
        decimal resolvedHeight = latestMetric?.CurrentHeightCm ?? nutProfile?.HeightCm ?? 0m;

        double currentWeight = (double)resolvedWeight;
        double height = (double)resolvedHeight;

        // 2. Age computation
        int age = 0;
        if (profile.BirthDate.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
             age = today.Year - profile.BirthDate.Value.Year;
            if (profile.BirthDate.Value > today.AddYears(-age)) age--;
        }

        // 3. BMI computation (gamit ang latest weight/height)
        var (bmiValue, bmiCategory) = NutritionCalculator.CalculateBmi(currentWeight, height);

        // 4. Parse comma-separated strings safely
        var selectedPrograms = onboarding?.SelectedPrograms?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();

        var fitnessGoals = onboarding?.FitnessGoals?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Trim()).Where(s=> !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();

        // 5. Aggregations (Workouts & Sessions)
        var userProgramIds = await _db.UsrUserProgramInstances
            .Where(up => up.UserId == userId)
            .Select(up => up.ProgramId)
            .Distinct()
            .ToListAsync();

        int totalWorkouts = userProgramIds.Any()
            ? await _db.WrkProgramTemplateDaytypeWorkouts
                    .CountAsync(w => userProgramIds.Contains(w.ProgramId))
            : 0;

        int totalProgramSessions = userProgramIds.Any() // default
            ? await _db.WrkProgramTemplateDays
                    .CountAsync(d => userProgramIds.Contains(d.ProgramId))
            : 28; // default fallback

        int completedSessions = await _db.UsrUserWorkoutSessions
            .CountAsync(s => s.UserId == userId);

        // 6. Nutritional Goal & Target
        string nutritionGoal = onboarding?.BodyGoal ?? cycle?.GoalType ?? "MAINTAIN_WEIGHT";
        double targetWeightKg = (double)(nutProfile?.TargetWeightKg ?? 0);

        // 7. Avatar URL Handling
        string? finalAvatarUrl = null;
        if (!string.IsNullOrEmpty(profile.AvatarUrl))
        {
            if (profile.AvatarUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                finalAvatarUrl = profile.AvatarUrl;                             // Appwrite → passthrough
            else
                finalAvatarUrl = $"{baseUrl}/{profile.AvatarUrl.TrimStart('/')}"; // Legacy → prefix
        }

        // 8. Map to DTO
        return new UserProfileResponse
        {
            Name = profile.Name ?? "User",
            Username = profile.Username ?? "user",
            AvatarUrl = finalAvatarUrl,
            Gender = profile.Gender ?? "-",
            Age = age,
            HeightCm = height,
            WeightKg = currentWeight,
            TargetWeightKg = targetWeightKg, // latest target weight na ang ginagamit
            BMI = bmiValue,
            BmiCategory = bmiCategory,
            NutritionGoal = nutritionGoal,
            GoalSubtitle = cycle?.GoalType?.Replace("_", " ").ToUpper() ?? "MAINTAIN WEIGHT",
            TotalWorkouts = totalWorkouts,
            TotalSessions = completedSessions,
            CompletedSessions = completedSessions,
            TotalProgramSessions = totalProgramSessions,
            SelectedPrograms = selectedPrograms,
            FitnessGoals = fitnessGoals,
            DailyCalorieTarget = (int)(cycle?.DailyTargetNetCalories ?? 0m),
            ProteinG = (double)(cycle?.ProteinTargetG ?? 0m),
            CarbsG = (double)(cycle?.CarbsTargetG ?? 0m),
            FatsG = (double)(cycle?.FatsTargetG ?? 0m),
        };
    }
}