using FlexiFit.Api.Dtos;
using FlexiFit.Api.Extensions;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Mappers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FlexiFit.Api.Services;

public class MobileService
{
    private readonly FlexiFitDbContext _context;
    private readonly IDbContextFactory<FlexiFitDbContext> _contextFactory;
    private readonly ILogger<MobileService> _logger;
    private readonly IMemoryCache _cache;
    private readonly NutritionCalculator _nutritionCalculator;
    private readonly MealPlanGenerator _mealPlanGenerator;
    private readonly ProgramService _programService;
    private readonly WorkoutPoolSelector _poolSelector;

    public MobileService(
        FlexiFitDbContext context,
        IDbContextFactory<FlexiFitDbContext> contextFactory,
        ILogger<MobileService> logger,
        IMemoryCache memoryCache,
        NutritionCalculator nutritionCalculator,
        MealPlanGenerator mealPlanGenerator,
        ProgramService programService,
        WorkoutPoolSelector poolSelector)
    {
        _context = context;
        _contextFactory = contextFactory;
        _logger = logger;
        _cache = memoryCache;
        _nutritionCalculator = nutritionCalculator;
        _mealPlanGenerator = mealPlanGenerator;
        _programService = programService;
        _poolSelector = poolSelector;
    }

    // ── 1. BOOTSTRAP ──────────────────────────────────────────────
    public async Task<object> GetBootstrapAsync(int userId)
    {
        var user = await _context.UsrUsers.FindAsync(userId);
        var onboardingDetail = await _context.UsrUserOnboardingDetails.FirstOrDefaultAsync(x => x.UserId == userId);
        bool isProfileComplete = user?.Status == "ACTIVE";

        var activeSession = await _context.UsrUserWorkoutSessions
            .Where(s => s.UserId == userId && s.Status == "PENDING")
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.SessionId, s.WorkoutDay, s.Status, exerciseCount = _context.UsrUserSessionWorkouts.Count(sw => sw.SessionId == s.SessionId) })
            .FirstOrDefaultAsync();

        var hasProgram = await _context.UsrUserProgramInstances.AnyAsync(p => p.UserId == userId && p.Status == "ACTIVE");

        return new
        {
            profileComplete = isProfileComplete,
            userId = userId,
            status = new { code = user?.Status ?? "NEW", hasActiveProgram = hasProgram, currentWorkoutSession = activeSession }
        };
    }

    // ── 2. DASHBOARD ──────────────────────────────────────────────
    public async Task<DashboardResponseDto> GetDashboardAsync(int userId, string baseUrl)
    {
        var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

        var user = await _context.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
        var profile = await _context.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
        var metrics = await _context.UsrUserMetrics.Where(m => m.UserId == userId).OrderByDescending(m => m.RecordedAt).FirstOrDefaultAsync();
        var target = await _context.NtrUserCycleTargets.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync();
        var latestVersion = await _context.UsrUserProfileVersions.Where(v => v.UserId == userId && v.IsCurrent == true).OrderByDescending(v => v.CreatedAt).FirstOrDefaultAsync();
        
        var activePrograms = await _programService.GetActiveProgramsAsync(userId);
        UsrUserProgramInstance? activeProgram = null;
        if (activePrograms.Any())
        {
            // Include "SKIPPED" so skipped programs also yield focus to the next one
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var processedInstanceIds = await _context.WktWorkoutCalendars
                .Where(w => w.UserId == userId && w.PlanDate == today && (w.Status == "DONE" || w.Status == "SKIPPED"))
                .Select(w => w.InstanceId)
                .ToListAsync();

            // Kunin ang UNANG program na HINDI pa tapos/skipped today
            activeProgram = activePrograms.FirstOrDefault(p => !processedInstanceIds.Contains(p.InstanceId)) 
                        ?? activePrograms.First(); // Fallback: kung tapos na lahat, ipakita ang first program
        }

        var dashboardData = new DashboardResponseDto
        {
            Name = !string.IsNullOrWhiteSpace(profile?.Name) ? profile.Name : (user?.Name ?? "Champion"),
            UserName = !string.IsNullOrWhiteSpace(profile?.Username) ? profile.Username : (user?.Username ?? "user"),
            UserAvatar = (profile != null && !string.IsNullOrWhiteSpace(profile.AvatarUrl)) ? (profile.AvatarUrl.StartsWith("http") ? profile.AvatarUrl : $"{baseUrl}/{profile.AvatarUrl.TrimStart('/')}") : $"{baseUrl}/uploads/avatars/default.png",
            FitnessLevel = activeProgram?.FitnessLevelAtStart ?? (latestVersion?.FitnessLevelSelected ?? "Beginner"),
            Goal = latestVersion?.GoalSelected ?? "LOSE"
        };

        // BMI Calculation (Gamit ang centralized NutritionCalculator)
        var (bmiValue, bmiStatus) = NutritionCalculator.CalculateBmi((double)(metrics?.CurrentWeightKg ?? 0), (double)(metrics?.CurrentHeightCm ?? 0));
        dashboardData.BmiData = new BmiDataDto { Value = bmiValue, Status = bmiStatus };

        var todayLog = await _context.NtrDailyLogs.FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);
        var waterMl = await _context.NtrWaterLogs.Where(w => w.UserId == userId && w.LogDate == todayDateOnly).SumAsync(w => (int?)w.WaterMl) ?? 0;
        var cycleTarget = await _context.NtrUserCycleTargets.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync();

        int safeTarget = todayLog?.TargetNetCalories ?? (cycleTarget?.DailyTargetNetCalories ?? 2000);
        int safeIntake = todayLog?.CaloriesConsumed ?? 0;
        double safeBurned = (double)(todayLog?.CaloriesBurned ?? 0);
        int safeNet = todayLog?.NetCalories ?? 0;
        int safeRemaining = safeTarget - safeNet;

        dashboardData.Nutrition = new NutritionDataDto
        {
            TargetCalories = safeTarget,
            Intake = safeIntake,
            Burned = safeBurned,
            NetCalories = safeNet,
            Remaining = safeRemaining,
            WaterGlasses = waterMl / 250,
            WaterTarget = 8
        };

        // Workout Data
        if (activeProgram != null)
        {
            int currentDay = activeProgram.CurrentDayNo;
            int currentWeek = ((currentDay - 1) / 7) + 1;
            int templateDayNo = ((currentDay - 1) % 7) + 1;

            var dayDef = await _context.WrkProgramTemplateDays
                .Where(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo && d.WeekNo == 1)
                .Select(d => d.DayType).FirstOrDefaultAsync();

            if (dayDef != null)
            {
                bool isCompletedToday = await _context.UsrUserWorkoutSessions.AnyAsync(s => s.UserId == userId && s.ProgramInstanceId == activeProgram.InstanceId && s.WorkoutDay == currentDay && s.Status == "COMPLETED");

                var rawWorkouts = await (from tw in _context.WrkProgramTemplateDaytypeWorkouts
                                         join w in _context.WrkWorkouts on tw.WorkoutId equals w.WorkoutId
                                         where tw.ProgramId == activeProgram.ProgramId && tw.WeekNo == currentWeek && tw.DayType == dayDef
                                         orderby tw.WorkoutOrder
                                         select new { w.WorkoutId, w.WorkoutName, w.ImgFilename, w.Category, w.MuscleGroup, w.Duration, w.CaloriesBurned, tw.SetsDefault, tw.RepsDefault })
                                         .Take(2).ToListAsync();

                dashboardData.UpcomingWorkouts = rawWorkouts.Select(w => new WorkoutExerciseDto
                {
                    Id = w.WorkoutId,
                    Name = w.WorkoutName,
                    ImageFileName = !string.IsNullOrEmpty(w.ImgFilename) ? WorkoutMapper.ResolveImageUrl(baseUrl, w.ImgFilename) : "",
                    MuscleGroup = w.MuscleGroup ?? "Full Body",
                    Sets = w.SetsDefault,
                    Reps = w.RepsDefault,
                    DurationMinutes = w.Duration ?? 10,
                    Calories = (int)(w.CaloriesBurned ?? 50),
                    IsCompleted = isCompletedToday
                }).ToList();
            }
        }

        // Meal Preview
        List<MealGroupDto> todayMeals = new();
        if (todayLog != null)
        {
            var loggedMeals = await _context.NtrDailyMealItemLogs.Include(i => i.Food).Where(i => i.DailyLogId == todayLog.DailyLogId).OrderBy(i => i.SortOrder).ToListAsync();
            todayMeals = loggedMeals.GroupBy(i => i.MealType).OrderBy(g => NutritionMapper.GetMealOrder(g.Key))
                .Select(g => new MealGroupDto
                {
                    TemplateMealId = 0,
                    MealType = g.Key,
                    Status = "PENDING",
                    FoodItems = g.Select(i => new FoodItemDto
                    {
                        FoodId = i.FoodId,
                        Name = i.Food?.FoodName ?? "Unknown",
                        Description = i.Food?.Description ?? "",
                        ImageUrl = NutritionMapper.BuildFoodImageUrl(baseUrl, i.Food?.ImgFilename),
                        DietaryType = i.Food?.DietaryType ?? "balanced",
                        Qty = (double)i.Qty,
                        Unit = i.Food?.ServingUnit ?? "serving",
                        Calories = (double)i.Calories,
                        Protein = (double)i.ProteinG,
                        Carbs = (double)i.CarbsG,
                        Fats = (double)i.FatsG
                    }).ToList()
                }).ToList();
        }
        dashboardData.TodayMeals = todayMeals;
        return dashboardData;
    }

    // ── 3. ONBOARDING (Massive Transaction) ───────────────────────
    public async Task<object> SubmitOnboardingProfileAsync(int userId, OnboardingProfileRequest request)
    {
        // ── GUARD CLAUSE (Enforce max 3 programs limit) ──
        const int maxPrograms = 3;
        if(request.SelectedPrograms != null && request.SelectedPrograms.Count > maxPrograms)
        {
            throw new ArgumentException(
                $"Maximum of {maxPrograms} programs allowed during onboarding for your safety. " +
                $"Received: {request.SelectedPrograms.Count}. " +
                $"Please select at most {maxPrograms} programs.");
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // ==========================================
                // BATCH 1: CORE DATA (Profile, Cycle, DailyLog, Allergies)
                // ==========================================
                // --- 1. BASE PROFILE & USER SYNC (SMART FALLBACK) ---
                var user = await _context.UsrUsers.FindAsync(userId);
                var profile = await _context.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
                            ?? new UsrUserProfile { UserId = userId, CreatedAt = DateTime.UtcNow };

                if (user != null)
                {
                    // A. UPDATE MAIN USER TABLE (KUNG MAY LAMAN ANG REQUEST)
                    // Kung empty string ("") ang pinasa ng mobile, HINDI natin o-overwrite si "cj" sa SQL.
                    if (!string.IsNullOrWhiteSpace(request.Name))
                    {
                        user.Name = request.Name;
                    }

                    if (!string.IsNullOrWhiteSpace(request.Username))
                    {
                        user.Username = request.Username;
                    }

                    user.Status = "ACTIVE";
                    user.UpdatedAt = DateTime.UtcNow;
                    _context.Entry(user).State = EntityState.Modified;

                    // B. UPDATE PROFILE TABLE (PARA SA DASHBOARD DISPLAY)
                    // DITO ANG MAGIC: Kung walang input sa onboarding, hiramin si "cj" at "cy" sa User table.
                    profile.Name = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : user.Name;
                    profile.Username = !string.IsNullOrWhiteSpace(request.Username) ? request.Username : user.Username;
                }

                // C. GENDER & AVATAR
                profile.Gender = request.Gender;

                // Siguradong may default avatar link kung wala pang upload
                if (string.IsNullOrWhiteSpace(profile.AvatarUrl))
                {
                    profile.AvatarUrl = "uploads/avatars/default.png";
                }

                // D. BIRTHDATE LOGIC
                if (request.Age > 0)
                {
                    profile.BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-request.Age));
                }

                profile.UpdatedAt = DateTime.UtcNow;

                // I-save ang profile kung bago (Detached)
                if (_context.Entry(profile).State == EntityState.Detached)
                    _context.UsrUserProfiles.Add(profile);

                // --- DITO NA PAPASOK YUNG STEP 2 (PROFILE VERSION) ---

                // 2. OPTIMIZATION 1: ExecuteUpdateAsync imbes na i-load ang lahat ng versions sa memory (iwas N+1)
                await _context.UsrUserProfileVersions
                    .Where(v => v.UserId == userId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(v => v.IsCurrent, false));

                var profileVersion = new UsrUserProfileVersion
                {
                    UserId = userId,
                    FitnessLevelSelected = request.FitnessLevel ?? "Beginner",
                    GoalSelected = request.BodyGoal ?? "Strength",
                    IsCurrent = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.UsrUserProfileVersions.Add(profileVersion);

                // 3. ONBOARDING DETAILS
                var details = await _context.UsrUserOnboardingDetails.FirstOrDefaultAsync(d => d.UserId == userId)
                            ?? new UsrUserOnboardingDetail { UserId = userId, CreatedAt = DateTime.UtcNow };

                details.ActivityLevel = request.ActivityLevel;
                details.FitnessLevel = request.FitnessLevel;
                details.BodyGoal = request.BodyGoal;
                details.DietType = request.DietType;
                details.UpperBodyInjury = request.UpperBodyInjury;
                details.LowerBodyInjury = request.LowerBodyInjury;
                details.JointProblems = request.JointProblems;
                details.ShortBreath = request.ShortBreath;
                details.HealthNone = request.HealthNone;
                details.Environment = request.Environment != null ? string.Join(", ", request.Environment) : "GYM";
                details.FitnessGoals = request.FitnessGoals != null ? string.Join(", ", request.FitnessGoals) : "";
                details.UpdatedAt = DateTime.UtcNow;

                if (request.SelectedPrograms != null)
                {
                    details.SelectedPrograms = string.Join(", ", request.SelectedPrograms.Select(p => p.Name));
                }

                if (_context.Entry(details).State == EntityState.Detached) _context.UsrUserOnboardingDetails.Add(details);

                // --- 3.5 ALLERGIES (NEW) ---
                // OPTIMIZATION 2: ExecuteDeleteAsync + Batched Dictionary Lookup (Iwas Allergy N+1)
                await _context.NtrUserAllergies.Where(ua => ua.UserId == userId).ExecuteDeleteAsync();

                // Add new allergies from request
                if (request.Allergies != null && request.Allergies.Any())
                {
                    var existingAllergies = await _context.NtrAllergies
                        .Where(a => request.Allergies.Contains(a.AllergyName))
                        .ToDictionaryAsync(a => a.AllergyName);

                    var newAllergies = request.Allergies
                        .Where(name => !existingAllergies.ContainsKey(name))
                        .Select(name => new NtrAllergies { AllergyName = name })
                        .ToList();

                    if (newAllergies.Any())
                    {
                        _context.NtrAllergies.AddRange(newAllergies);

                        foreach (var na in newAllergies)
                            existingAllergies[na.AllergyName] = na;
                    }

                    var userAllergies = request.Allergies.Select(name => new NtrUserAllergies
                    {
                        UserId = userId,
                        AllergyId = existingAllergies[name].AllergyId
                    }).ToList();
                    _context.NtrUserAllergies.AddRange(userAllergies);
                }

                // --- 4. NUTRITION ENGINE (SEEDING INITIAL MEALS) ---
                var macros = _nutritionCalculator.Calculate(
                    weightKg: (double)request.WeightKg,
                    heightCm: (double)request.HeightCm,
                    age: request.Age > 0 ? request.Age : 25,
                    gender: request.Gender,
                    activityLevel: request.ActivityLevel,
                    goal: request.BodyGoal,
                    dietaryType: request.DietType
                );

                double calorieTarget = macros.Calories;
                decimal proteinTarget = macros.ProteinG;
                decimal carbsTarget = macros.CarbsG;
                decimal fatsTarget = macros.FatsG;

                string goal = (request.BodyGoal ?? "").ToUpper();

                // A. Add Metrics
                var metrics = new UsrUserMetric
                {
                    UserId = userId,
                    CurrentWeightKg = request.WeightKg,
                    CurrentHeightCm = request.HeightCm,
                    FitnessGoal = goal,
                    NutritionGoal = (request.DietType ?? "BALANCED").ToUpper(),
                    CalorieTarget = (int)calorieTarget,
                    ProteinTargetG = (int)proteinTarget,
                    CarbsTargetG = (int)carbsTarget,
                    FatsTargetG = (int)fatsTarget,
                    RecordedAt = DateTime.UtcNow
                };
                _context.UsrUserMetrics.Add(metrics);

                // B. Add Cycle Target
                var cycleTarget = new NtrUserCycleTarget
                {
                    UserId = userId,
                    DailyTargetNetCalories = (int)calorieTarget,
                    GoalType = goal,
                    ProteinTargetG = proteinTarget,
                    CarbsTargetG = carbsTarget,
                    FatsTargetG = fatsTarget,
                    WeeksInCycle = 4,
                    StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    CreatedAt = DateTime.UtcNow
                };
                _context.NtrUserCycleTargets.Add(cycleTarget);

                // 💾 SAVE POINT 1: Database generates the real CycleId and ProfileVersionId here!
                await _context.SaveChangesAsync();

                // --- ADD/UPDATE NUTRITION PROFILE ---
                var nutProfile = await _context.NtrUserNutritionProfiles
                    .FirstOrDefaultAsync(p => p.UserId == userId);
                if (nutProfile == null)
                {
                    nutProfile = new NtrUserNutritionProfile
                    {
                        UserId = userId,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.NtrUserNutritionProfiles.Add(nutProfile);
                }

                nutProfile.Age = request.Age;
                nutProfile.HeightCm = (decimal)request.HeightCm;
                nutProfile.WeightKg = (decimal)request.WeightKg;
                nutProfile.TargetWeightKg = (decimal)request.TargetWeightKg;

                // 🔁 Map activity level to allowed database values
                string rawActivity = request.ActivityLevel ?? request.ActivityLevel ?? "Sedentary";
                nutProfile.ActivityLevel = NutritionCalculator.NormalizeActivity(rawActivity);

                nutProfile.DietaryType = request.DietType?.ToUpper() ?? "BALANCED";
                nutProfile.NutritionGoal = request.BodyGoal?.ToUpper() ?? "MAINTAIN";
                nutProfile.IsProfileComplete = true;
                nutProfile.UpdatedAt = DateTime.UtcNow;

                if (_context.Entry(nutProfile).State == EntityState.Detached)
                    _context.NtrUserNutritionProfiles.Add(nutProfile);

                // --- 5. CREATE DAILY LOG (Para sa Dashboard Today) ---
                var dailyLog = new NtrDailyLog
                {
                    UserId = userId,
                    CycleId = cycleTarget.CycleId,
                    PlanDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    TargetNetCalories = (int)calorieTarget,
                    CaloriesConsumed = 0,
                    CaloriesBurned = 0,
                    GoalMet = false
                };
                _context.NtrDailyLogs.Add(dailyLog);

                // 💾 SAVE POINT 2: Dito nagsave para ma-generate profileVersionId, cycleId, and dailyLogId in ONE trip.
                await _context.SaveChangesAsync();

                // ==========================================
                // BATCH 2: PREPARE CALENDAR & MEALS
                // ==========================================
                // --- 6. SEED MEAL PLAN CALENDAR (OPTIMIZED 3: Iwas Calendar N+1 Loop) ---
                var dietTypeUpper = (request.DietType ?? "BALANCED").ToUpper();
                var matchedTemplate = await _context.NtrMealTemplates
                    .FirstOrDefaultAsync(t => t.DietaryType == dietTypeUpper)
                    ?? await _context.NtrMealTemplates.OrderBy(t => t.TemplateId).FirstOrDefaultAsync();

                int safeTemplateId = matchedTemplate?.TemplateId ?? 1;

                // Kinukuha na ang 7 days ONCE lang, hindi na 27 beses sa loob ng loop
                var firstSelectedProgramName = request.SelectedPrograms?.FirstOrDefault()?.Name?.Trim().ToLower().Replace(" ", "");
                var seedProgram = await _context.WrkProgramTemplates
                    .FirstOrDefaultAsync(t => t.ProgramName.Replace(" ", "").ToLower() == firstSelectedProgramName);

                // Fallback to 95 kung hindi mahanap para hindi maputol ang calendar generation
                int seedProgramId = seedProgram?.ProgramId ?? 95;

                // HANDLES DUPLICATES AUTOMATICALLY (Iwas "Key: 1" error)
                // Kunin muna as List, tapos GroupBy para kunin lang ang FIRST occurrence ng bawat DayNo
                var templateDaysDict = await _context.WrkProgramTemplateDays
                    .Where(t => t.ProgramId == seedProgramId && t.WeekNo == 1) 
                    .ToDictionaryAsync(t => t.DayNo);

                var calendarEntries = new List<NtrMealPlanCalendar>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                // Day 1
                calendarEntries.Add(new NtrMealPlanCalendar
                {
                    CycleId = cycleTarget.CycleId,
                    PlanDate = today,
                    WeekNo = 1,
                    DayNo = 1,
                    TemplateId = safeTemplateId,
                    VariationCode = "STD",
                    Status = "PENDING",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                // Future Days (2-28)
                for (int d = 2; d <= 28; d++)
                {
                    // Ginamit ang dictionary imbes na mag-query ulit sa database (Iwas N+1)
                    int dayNoKey = ((d - 1) % 7) + 1; // Maps 1-7 repeatedly
                    var templateDayRecord = templateDaysDict.GetValueOrDefault(dayNoKey);
                    bool isWorkout = templateDayRecord != null && !templateDayRecord.DayType.Contains("REST");

                    calendarEntries.Add(new NtrMealPlanCalendar
                    {
                        CycleId = cycleTarget.CycleId,
                        PlanDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(d - 1)),
                        WeekNo = ((d - 1) / 7) + 1,
                        DayNo = d,
                        TemplateId = safeTemplateId, // para sa Foreign Key
                        IsWorkoutDay = isWorkout,
                        Status = "PENDING",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                _context.NtrMealPlanCalendars.AddRange(calendarEntries);

                // --- 7. MACRO-BALANCED MEAL SEEDING --- (Added to Context, No Save Yet)
                var seedResult = await _mealPlanGenerator.GenerateForDayAsync(
                    userId: userId,
                    dailyLogId: dailyLog.DailyLogId,
                    targets: macros);

                if (seedResult.ItemLogs.Any())
                    _context.NtrDailyMealItemLogs.AddRange(seedResult.ItemLogs);

                foreach (var (mealCode, totals) in seedResult.MealSummaries)
                {
                    _context.NtrDailyMealLogs.Add(new NtrDailyMealLog
                    {
                        DailyLogId = dailyLog.DailyLogId,
                        MealType = mealCode,
                        Calories = totals.cal,
                        ProteinG = totals.prot,
                        CarbsG = totals.carb,
                        FatsG = totals.fats
                    });
                }

                // ==========================================
                // BATCH 3: PROGRAM INSTANCES
                // ==========================================
                // --- 5. WORKOUT ACTIVATION (OPTIMIZED 4: Iwas Workout N+1 at Excessive Saves) ---
                if (request.SelectedPrograms != null && request.SelectedPrograms.Any())
                {
                    // Let WorkoutPoolSelector handle the target burn calculation
                    double targetBurnForSession = await _poolSelector.GetTargetBurnAsync(userId);

                    // Replace existing active programs
                    await _context.UsrUserProgramInstances
                        .Where(p => p.UserId == userId && p.Status == "ACTIVE")
                        .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Status, "REPLACED"));

                    // 2. PRE-FETCH: Kunin ang lahat ng templates na kailangan in ONE query
                    var requestedLevel = (request.FitnessLevel ?? "Beginner").Trim().ToLower();
                    var programNames = request.SelectedPrograms.Select(p => (p.Name ?? "").Trim().ToLower().Replace(" ", "")).ToList();

                    var templates = await _context.WrkProgramTemplates
                        .Where(t => programNames.Contains(t.ProgramName.Replace(" ", "").ToLower()) &&
                                    (t.FitnessLevel.ToLower() == requestedLevel ||
                                    (request.IsRehab && (t.FitnessLevel.ToLower() == "rehab level" || t.ProgramName.Contains("Rehab", StringComparison.OrdinalIgnoreCase)))))
                        .ToListAsync();

                    // Helper function para maghanap in-memory (Zero DB hits)
                    WrkProgramTemplate? FindTemplate(string inputName)
                    {
                        var normName = inputName.Trim().ToLower().Replace(" ", "");
                        var match = templates.FirstOrDefault(t => t.ProgramName.Replace(" ", "").ToLower() == normName && t.FitnessLevel.ToLower() == requestedLevel);
                        if (match != null) return match;

                        if (request.IsRehab)
                        {
                            match = templates.FirstOrDefault(t => t.ProgramName.Replace(" ", "").ToLower() == normName && t.FitnessLevel.ToLower() == "rehab level");
                            if (match != null) return match;

                            match = templates.FirstOrDefault(t => t.ProgramName.Contains("Rehab", StringComparison.OrdinalIgnoreCase));
                            if (match != null) return match;
                        }
                        return null;
                    }

                    var instanceWrappers = new List<(UsrUserProgramInstance Instance, WrkProgramTemplate Template)>();

                    // Build all instances in memory first
                    foreach (var progDto in request.SelectedPrograms)
                    {
                        var template = FindTemplate(progDto.Name ?? "");
                        if (template == null) continue;

                        var instance = new UsrUserProgramInstance
                        {
                            UserId = userId,
                            ProgramId = template.ProgramId,
                            ProfileVersionId = profileVersion.ProfileVersionId,
                            CycleNo = cycleTarget.CycleId,
                            Status = "ACTIVE",
                            CurrentDayNo = 1,
                            FitnessLevelAtStart = request.FitnessLevel,
                            CreatedAt = DateTime.UtcNow
                        };
                        instanceWrappers.Add((instance, template));
                    }

                    if (!instanceWrappers.Any()) return new { message = "No valid programs found.", status = "ACTIVE" }; 

                    _context.UsrUserProgramInstances.AddRange(instanceWrappers.Select(w => w.Instance));

                    // 💾 SAVE POINT 3: Generates InstanceId needed for Sessions
                    await _context.SaveChangesAsync();

                    // ==========================================
                    // BATCH 4: WORKOUT SESSIONS
                    // ==========================================
                    // 3. PRE-FETCH: Kunin ang Day 1 structures at Workout Pools in ONE query
                    var programIds = instanceWrappers.Select(w => w.Template.ProgramId).Distinct().ToList();
                    
                    // Use GroupBy to handle multiple Day 1 entries per ProgramId (e.g., across different weeks)
                    var dayStructuresList = await _context.WrkProgramTemplateDays
                        .Where(d => programIds.Contains(d.ProgramId) && d.DayNo == 1 && d.WeekNo == 1)
                        .ToListAsync();

                    var dayStructures = dayStructuresList
                        .GroupBy(d => d.ProgramId)
                        .ToDictionary(g => g.Key, g => g.First()); // Kunin lang ang first occurrence per ProgramId

                    var neededDayTypes = dayStructures.Values.Select(d => d.DayType ?? "WORKOUT").Distinct().ToList();

                    var workoutPools = await _context.WrkProgramTemplateDaytypeWorkouts
                        .Include(dw => dw.Workout)
                        .Where(dw => programIds.Contains(dw.ProgramId) 
                            && neededDayTypes.Contains(dw.DayType ?? "WORKOUT")
                            && dw.WeekNo == 1)
                        .ToListAsync();

                    // Gamitin ang unique list para sa dictionary
                    var workoutPoolDict = workoutPools
                        .GroupBy(dw => new { dw.ProgramId, dw.WeekNo, DayType = dw.DayType ?? "WORKOUT" })
                        .ToDictionary(
                            g => (ProgramId: g.Key.ProgramId, WeekNo: g.Key.WeekNo, DayType: g.Key.DayType), 
                            g => g.ToList());

                    var sessionWrappers = new List<(UsrUserWorkoutSession Session, WrkProgramTemplate Template)>();
                    var sessionInstancesToCreate = new List<UsrUserSessionInstance>();

                    // Build sessions in memory
                    foreach (var wrapper in instanceWrappers)
                    {
                        if (!dayStructures.TryGetValue(wrapper.Template.ProgramId, out var dayStructure)) continue;
                        var dayType = dayStructure.DayType ?? "WORKOUT";

                        var session = new UsrUserWorkoutSession
                        {
                            UserId = userId,
                            ProgramInstanceId = wrapper.Instance.InstanceId,
                            WorkoutDay = 1,
                            Status = "PENDING",
                            CreatedAt = DateTime.UtcNow
                        };
                        sessionWrappers.Add((session, wrapper.Template));

                        sessionInstancesToCreate.Add(new UsrUserSessionInstance
                        {
                            InstanceId = wrapper.Instance.InstanceId,
                            MonthNo = 1,
                            WeekNo = 1,
                            DayNo = 1,
                            DayType = dayType,
                            Status = "PENDING",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    _context.UsrUserWorkoutSessions.AddRange(sessionWrappers.Select(w => w.Session));
                    _context.UsrUserSessionInstances.AddRange(sessionInstancesToCreate);

                    // 💾 SAVE POINT 4: Generates SessionId needed for SessionWorkouts
                    await _context.SaveChangesAsync();

                    // ==========================================
                    // BATCH 5: SESSION WORKOUTS (Uses generated SessionId)
                    // ==========================================
                    // 4. Build SessionWorkouts IN-MEMORY (Filtering & Shuffling)

                    // PRE-FETCH ALL ACTIVE WARMUPS ONCE (Iwas N+1 sa loob ng loop)

                    var allWarmups = await _context.WrkWorkouts
                        .AsNoTracking()
                        .Where(w => w.Category == "WARMUP" && w.IsActive == true)
                        .ToListAsync();

                    var sessionWorkoutsToCreate = new List<UsrUserSessionWorkout>();

                    foreach (var wrapper in sessionWrappers)
                    {
                        int order = 1;
                        int currentWeek = ((wrapper.Session.WorkoutDay - 1) / 7) + 1;
                        var dayStructure = dayStructures[wrapper.Template.ProgramId];
                        var dayType = dayStructure.DayType ?? "WORKOUT";

                        // ── 1. WARMUP SEEDING (Rehab-aware & Case-insensitive) ──
                        bool isRehab = ProgramRules.IsRehabTemplate(wrapper.Template) || request.IsRehab;
                        var warmupLevels = new List<string> { requestedLevel, wrapper.Template.FitnessLevel ?? "Beginner" };
                        if (isRehab) warmupLevels.Add(ProgramRules.RehabLevel);

                        var warmupNorm = warmupLevels
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => l.Trim().ToLower())
                            .ToHashSet();

                        var warmups = allWarmups
                            .Where(w => warmupNorm.Contains((w.DifficultyLevel ?? "").Trim().ToLower()))
                            .OrderBy(_=> Random.Shared.Next())
                            .Take(2)
                            .ToList();

                        foreach (var w in warmups)
                        {
                            sessionWorkoutsToCreate.Add(new UsrUserSessionWorkout
                            {
                                SessionId = wrapper.Session.SessionId,
                                WorkoutId = w.WorkoutId,
                                Sets = 2,
                                Reps = 12,
                                OrderNo = order++,
                                LoadKg = 0
                            });
                        }

                        // ── MAIN WORKOUTS (Using WorkoutPoolSelector for consistency) ──
                        var key = (ProgramId: wrapper.Template.ProgramId, WeekNo: currentWeek, DayType: dayType);
    
                        if (workoutPoolDict.TryGetValue(key, out var dayWorkoutsPool))
                        {
                            // Gumamit ng WorkoutPoolSelector.Pick imbes na manual shuffle/filter
                            var picked = WorkoutPoolSelector.Pick(
                                dayWorkoutsPool, 
                                new[] { requestedLevel }, 
                                targetBurnForSession, 
                                logger: _logger);
                                
                            sessionWorkoutsToCreate.AddRange(
                                WorkoutPoolSelector.BuildSessionWorkouts(wrapper.Session.SessionId, picked, startOrder: order));
                        }
                    }

                    // ADDED TO CONTEXT, HINDI PA SINE-SAVE
                    _context.UsrUserSessionWorkouts.AddRange(sessionWorkoutsToCreate);
                }

                // ==========================================
                // FINAL SAVE: 
                // ==========================================
                // --- 6. FINALIZE ---
                if (user != null) user.Status = "ACTIVE";

                // 💾 SAVE POINT 5: FINAL
                // Isang malaking batch na nagse-save ng Calendar (28 rows), Meal Logs, Meal Item Logs, 
                // Session Workouts, at User Status Update nang SABAY-SABAY
                await _context.SaveChangesAsync(); // FINAL SAVE POINT

                await tx.CommitAsync();
                return new { message = "Onboarding complete! Dashboard initialized.", status = "ACTIVE" };
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                throw new Exception($"Onboarding failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        });
    }

    // ── 4. RESET ONBOARDING (Cascading Delete) ────────────────────
    public async Task<object> ResetOnboardingAsync(int userId)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            // STEP 0: Early Exit Guard (bago mag open ng transaction)
            var user = await _context.UsrUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return new { message = "User not found." };

            if (user.Status == "PENDING_ONBOARDING")
                return new { message = "User data is already cleared. No action taken."};

            // Sisimulan na ang trasaction dahil may mga dependent child records na kailangang i-delete sa tamang order
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // ============================================================
                // STEP 1: WORKOUT SESSION WORKOUTS (deepest child)
                // ============================================================
                var workoutSessionIds = await _context.UsrUserWorkoutSessions
                    .Where(x => x.UserId == userId)
                    .Select(x => x.SessionId)
                    .ToListAsync();

                if (workoutSessionIds.Any())
                {
                    await _context.UsrUserSessionWorkouts
                        .Where(x => workoutSessionIds.Contains(x.SessionId))
                        .ExecuteDeleteAsync();
                }

                // ============================================================
                // STEP 2: PROGRAM INSTANCE CHILDREN
                // ============================================================
                var programInstanceIds = await _context.UsrUserProgramInstances
                    .Where(x => x.UserId == userId)
                    .Select(x => x.InstanceId)
                    .ToListAsync();

                if (programInstanceIds.Any())
                {
                    // 2a. SessionInstances (FK = InstanceId)
                    await _context.UsrUserSessionInstances
                        .Where(x => programInstanceIds.Contains(x.InstanceId))
                        .ExecuteDeleteAsync();

                    // 2b. DailyProgressLogs (FK = InstanceId)
                    await _context.DailyProgressLogs
                        .Where(x => programInstanceIds.Contains(x.InstanceId))
                        .ExecuteDeleteAsync();
                }

                // ============================================================
                // STEP 3: WORKOUT SESSIONS & PROGRESS
                // ============================================================
                await _context.UsrUserWorkoutSessions
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.UsrUserWorkoutProgresses
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 4: WORKOUT CALENDARS
                // ============================================================
                await _context.WktWorkoutCalendars
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 5: PROGRAM INSTANCES (safe na ngayon)
                // ============================================================
                await _context.UsrUserProgramInstances
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 6: NUTRITION — MEAL ITEM/MEAL LOGS (deepest)
                // ============================================================
                var dailyLogIds = await _context.NtrDailyLogs
                    .Where(x => x.UserId == userId)
                    .Select(x => x.DailyLogId)
                    .ToListAsync();

                if (dailyLogIds.Any())
                {
                    await _context.NtrDailyMealItemLogs
                        .Where(x => dailyLogIds.Contains(x.DailyLogId))
                        .ExecuteDeleteAsync();

                    await _context.NtrDailyMealLogs
                        .Where(x => dailyLogIds.Contains(x.DailyLogId))
                        .ExecuteDeleteAsync();
                }

                // ============================================================
                // STEP 7: NUTRITION — DAILY LOGS
                // ============================================================
                await _context.NtrDailyLogs
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 8: NUTRITION — CYCLE TARGETS & MEAL PLAN CALENDARS
                // ============================================================
                var cycleIds = await _context.NtrUserCycleTargets
                    .Where(x => x.UserId == userId)
                    .Select(x => x.CycleId)
                    .ToListAsync();

                if (cycleIds.Any())
                {
                    await _context.NtrMealPlanCalendars
                        .Where(x => cycleIds.Contains(x.CycleId))
                        .ExecuteDeleteAsync();
                }

                await _context.NtrUserCycleTargets
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 9: NUTRITION — OTHER
                // ============================================================
                await _context.NtrWaterLogs
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.NtrUserNutritionProfiles
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.NtrUserAllergies
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 10: ACTIVITY
                // ============================================================
                await _context.ActActivitySummaries
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 11: USER GENERAL ACHIEVEMENTS (FK → UsrUserProfile.UserId)
                // ============================================================
                await _context.UsrUserGeneralAchievements
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 12: PROGRAM ACHIEVEMENTS (child of ProfileVersion)
                // ============================================================
                await _context.UsrUserProgramAchievements
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 13: PROFILE & PROFILE VERSION
                // ============================================================
                await _context.UsrUserProfiles
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.UsrUserProfileVersions
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 14: METRICS, ONBOARDING, NOTIFICATION SETTINGS
                // ============================================================
                await _context.UsrUserMetrics
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.UsrUserOnboardingDetails
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.UsrUserNotificationSettings
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 15: DEVICE TOKENS & NOTIFICATION HISTORY
                // ============================================================
                await _context.UsrDeviceTokens
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                await _context.UsrNotificationHistories
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();

                // ============================================================
                // STEP 16: RESET USER STATUS
                // ============================================================
                await _context.UsrUsers
                    .Where(u => u.UserId == userId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(u => u.Status, "PENDING_ONBOARDING")
                        .SetProperty(u => u.UpdatedAt, DateTime.UtcNow));

                await tx.CommitAsync();
                return new { message = "Complete user data reset successfully." };
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                throw; // ipapasa sa global exception handler para sa logging at response formatting
            }
        });
    }
}