using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class ProfileService
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<ProfileService> _logger;
    private readonly NutritionCalculator _nutritionCalculator;
    private readonly IBlobService _blobService;

    public ProfileService(
        FlexiFitDbContext db, 
        ILogger<ProfileService> logger,
        NutritionCalculator nutritionCalculator,
        IBlobService blobService)
    {
        _db = db;
        _logger = logger;
        _nutritionCalculator = nutritionCalculator;
        _blobService = blobService;
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

    // Encapsulated profile status check for external use (e.g., AuthController)
    public async Task<(bool UserExists, bool IsProfileCompleted)> GetProfileStatusAsync(string firebaseUid, CancellationToken ct = default)
    {
            // Find local user mapped to Firebase UID
            var dbUser = await _db.UsrUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, ct);

            if (dbUser == null)
            {
                return (false, false);
            }

            // Profile completion check (current version exists)
            var isProfileCompleted = await _db.UsrUserProfileVersions
                .AsNoTracking()
                .AnyAsync(p => p.UserId == dbUser.UserId && p.IsCurrent, ct);

            return (true, isProfileCompleted);
    }

    // ==========================================
    // PROFILE UPDATES
    // ==========================================
    public async Task<decimal> UpdateFullProfileAsync(int userId, UpdateOnboardingRequest request)
    {
        // 1. Update Basic Profile
        var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (profile != null)
        {
            profile.Name = request.Name;
            profile.Username = request.Username;
            profile.Gender = request.Gender;
            if (request.Age > 0)
            {
                profile.BirthDate = new DateOnly(DateTime.UtcNow.Year - request.Age, 1, 1);
            }
            profile.UpdatedAt = DateTime.UtcNow;
        }

        // 2. Update Nutrition Profile
        var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        if (nutProfile != null)
        {
            nutProfile.Age = (short)request.Age;
            nutProfile.HeightCm = (decimal)request.HeightCm;
            nutProfile.WeightKg = (decimal)request.WeightKg;
            nutProfile.TargetWeightKg = (decimal)request.TargetWeightKg;
            
            if (!string.IsNullOrEmpty(request.FitnessLifestyle)) nutProfile.ActivityLevel = request.FitnessLifestyle;
            if (!string.IsNullOrEmpty(request.DietaryType)) nutProfile.DietaryType = request.DietaryType;
            if (!string.IsNullOrEmpty(request.BodyCompGoal)) nutProfile.NutritionGoal = request.BodyCompGoal;
        }

        // 3. Recalculate Cycle Targets
        var cycle = await _db.NtrUserCycleTargets
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        if (cycle != null && nutProfile != null)
        {
            var targets = _nutritionCalculator.Calculate(
                weightKg: (double)request.WeightKg,
                heightCm: (double)request.HeightCm,
                age: request.Age,
                gender: request.Gender,
                activityLevel: nutProfile.ActivityLevel ?? "SEDENTARY",
                goal: request.BodyCompGoal ?? "MAINTAIN",
                dietaryType: nutProfile.DietaryType ?? "BALANCED",
                workoutCaloriesBurned: 0
            );

            cycle.DailyTargetNetCalories = targets.Calories;
            cycle.ProteinTargetG = targets.ProteinG;
            cycle.CarbsTargetG = targets.CarbsG;
            cycle.FatsTargetG = targets.FatsG;
            cycle.GoalType = request.BodyCompGoal;
            cycle.CreatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return cycle?.DailyTargetNetCalories ?? 0;
    }

    public async Task<string> UploadAvatarAsync(int userId, IFormFile file)
    {
        var avatarUrl = await _blobService.UploadFileAsync(file, containerName: "avatars");

        // I-update o i-create ang Profile record sa database
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
        return avatarUrl;
    }

    public async Task UpdateWeightAsync(int userId, UpdateWeightRequest request)
    {
        var nutProfile = await _db.NtrUserNutritionProfiles.FirstOrDefaultAsync(p => p.UserId == userId) 
            ?? throw new KeyNotFoundException("Nutrition profile not found.");
        
        var profile = await _db.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var cycle = await _db.NtrUserCycleTargets
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(c => c.UserId == userId);

        nutProfile.WeightKg = (decimal)request.NewWeight;

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

        if (cycle != null && profile != null)
        {
            var targets = _nutritionCalculator.Calculate(
                weightKg: (double)request.NewWeight,
                heightCm: (double)nutProfile.HeightCm,
                age: (int)nutProfile.Age,
                gender: profile.Gender ?? "MALE",
                activityLevel: nutProfile.ActivityLevel ?? "SEDENTARY",
                goal: (nutProfile.NutritionGoal ?? "MAINTAIN").ToUpper(),
                dietaryType: nutProfile.DietaryType ?? "BALANCED",
                workoutCaloriesBurned: 0
            );

            cycle.DailyTargetNetCalories = targets.Calories;
            cycle.ProteinTargetG = targets.ProteinG;
            cycle.CarbsTargetG = targets.CarbsG;
            cycle.FatsTargetG = targets.FatsG;
            cycle.CreatedAt = DateTime.UtcNow;

            newMetric.CalorieTarget = targets.Calories;
            newMetric.ProteinTargetG = targets.ProteinG;
            newMetric.CarbsTargetG = targets.CarbsG;
            newMetric.FatsTargetG = targets.FatsG;
        }

        await _db.SaveChangesAsync();
    }
}