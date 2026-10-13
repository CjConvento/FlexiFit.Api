using FlexiFit.Api.Dtos;
using FlexiFit.Api.Services;
using FlexiFit.Api.Extensions;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Mappers;
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

    public MobileService(
        FlexiFitDbContext context,
        IDbContextFactory<FlexiFitDbContext> contextFactory,
        ILogger<MobileService> logger,
        IMemoryCache memoryCache,
        NutritionCalculator nutritionCalculator,
        MealPlanGenerator mealPlanGenerator)
    {
        _context = context;
        _contextFactory = contextFactory;
        _logger = logger;
        _cache = memoryCache;
        _nutritionCalculator = nutritionCalculator;
        _mealPlanGenerator = mealPlanGenerator;
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
        var activeProgram = await _context.UsrUserProgramInstances.FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

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
            TargetCalories = safeTarget, Intake = safeIntake, Burned = safeBurned,
            NetCalories = safeNet, Remaining = safeRemaining,
            WaterGlasses = waterMl / 250, WaterTarget = 8
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
                    Id = w.WorkoutId, Name = w.WorkoutName,
                    ImageFileName = !string.IsNullOrEmpty(w.ImgFilename) ? WorkoutMapper.BuildWorkoutImageUrl(baseUrl, w.Category, w.ImgFilename) : "",
                    MuscleGroup = w.MuscleGroup ?? "Full Body", Sets = w.SetsDefault, Reps = w.RepsDefault,
                    DurationMinutes = w.Duration ?? 10, Calories = (int)(w.CaloriesBurned ?? 50), IsCompleted = isCompletedToday
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
                    TemplateMealId = 0, MealType = g.Key, Status = "PENDING",
                    FoodItems = g.Select(i => new FoodItemDto
                    {
                        FoodId = i.FoodId, Name = i.Food?.FoodName ?? "Unknown", Description = i.Food?.Description ?? "",
                        ImageUrl = NutritionMapper.BuildFoodImageUrl(baseUrl, i.Food?.DietaryType ?? "balanced", g.Key, i.Food?.ImgFilename),
                        DietaryType = i.Food?.DietaryType ?? "balanced", Qty = (double)i.Qty, Unit = i.Food?.ServingUnit ?? "serving",
                        Calories = (double)i.Calories, Protein = (double)i.ProteinG, Carbs = (double)i.CarbsG, Fats = (double)i.FatsG
                    }).ToList()
                }).ToList();
        }
        dashboardData.TodayMeals = todayMeals;
        return dashboardData;
    }

    // ── 3. ONBOARDING (Massive Transaction) ───────────────────────
    public async Task<object> SubmitOnboardingProfileAsync(int userId, OnboardingProfileRequest request)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
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

                    // --- DITO NA PAPASOK YUNG STEP 2 (PROFILE VERSION) MO BABE ---


                    // maayos na nagana
                    // 2. PROFILE VERSION
                    var oldVersions = await _context.UsrUserProfileVersions.Where(v => v.UserId == userId).ToListAsync();
                    foreach (var v in oldVersions) v.IsCurrent = false;

                    var profileVersion = new UsrUserProfileVersion
                    {
                        UserId = userId,
                        FitnessLevelSelected = request.FitnessLevel ?? "Beginner",
                        GoalSelected = request.BodyGoal ?? "Strength",
                        IsCurrent = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.UsrUserProfileVersions.Add(profileVersion);

                    await _context.SaveChangesAsync();


                    // nagana na ng maayos
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
                    // Remove existing user allergies
                    var existingAllergies = _context.NtrUserAllergies.Where(ua => ua.UserId == userId);
                    _context.NtrUserAllergies.RemoveRange(existingAllergies);

                    // Add new allergies from request
                    if (request.Allergies != null && request.Allergies.Any())
                    {
                        foreach (var allergyName in request.Allergies)
                        {
                            var allergy = await _context.NtrAllergies.FirstOrDefaultAsync(a => a.AllergyName == allergyName);
                            if (allergy == null)
                            {
                                allergy = new NtrAllergies { AllergyName = allergyName };
                                _context.NtrAllergies.Add(allergy);
                            }
                            _context.NtrUserAllergies.Add(new NtrUserAllergies { UserId = userId, AllergyId = allergy.AllergyId });
                        }
                        await _context.SaveChangesAsync();
                    }

                    // --- 4. NUTRITION ENGINE (SEEDING INITIAL MEALS) ---
                    var macros = _nutritionCalculator.Calculate(
                        weightKg:      (double)request.WeightKg,
                        heightCm:      (double)request.HeightCm,
                        age:           request.Age > 0 ? request.Age : 25,
                        gender:        request.Gender,
                        activityLevel: request.ActivityLevel,
                        goal:          request.BodyGoal,
                        dietaryType:   request.DietType
                    );

                    double calorieTarget = macros.Calories;
                    decimal proteinTarget = macros.ProteinG;
                    decimal carbsTarget   = macros.CarbsG;
                    decimal fatsTarget    = macros.FatsG;

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
                    string rawActivity = request.ActivityLevel?.ToUpper() ?? "SEDENTARY";
                    string mappedActivity = rawActivity switch
                    {
                        "SEDENTARY" => "SEDENTARY",
                        "LIGHTLY ACTIVE" => "LIGHTLY_ACTIVE",
                        "MODERATELY ACTIVE" => "ACTIVE",
                        "ACTIVE" => "ACTIVE",
                        "VERY ACTIVE" => "VERY_ACTIVE",
                        _ => "SEDENTARY"
                    };
                    nutProfile.ActivityLevel = mappedActivity;

                    nutProfile.DietaryType = request.DietType?.ToUpper() ?? "BALANCED";
                    nutProfile.NutritionGoal = request.BodyGoal?.ToUpper() ?? "MAINTAIN";
                    nutProfile.IsProfileComplete = true;
                    nutProfile.UpdatedAt = DateTime.UtcNow;

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
                    await _context.SaveChangesAsync();

                    // --- 6. SEED MEAL PLAN CALENDAR ---
                    var dietTypeUpper = (request.DietType ?? "BALANCED").ToUpper();
                    var matchedTemplate = await _context.NtrMealTemplates
                        .FirstOrDefaultAsync(t => t.DietaryType == dietTypeUpper)
                        ?? await _context.NtrMealTemplates.OrderBy(t => t.TemplateId).FirstOrDefaultAsync();

                    int safeTemplateId = matchedTemplate?.TemplateId ?? 1;

                    // Day 1 Entry
                    var calendarEntry = new NtrMealPlanCalendar
                    {
                        CycleId = cycleTarget.CycleId,
                        PlanDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        WeekNo = 1,
                        DayNo = 1,
                        TemplateId = safeTemplateId,
                        VariationCode = "STD",
                        Status = "PENDING",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.NtrMealPlanCalendars.Add(calendarEntry);

                    // Future Days (2-28) - FIXED: Nilagyan na ng TemplateId babe para hindi mag-error
                    for (int d = 2; d <= 28; d++)
                    {
                        var templateDayRecord = await _context.WrkProgramTemplateDays
                            .FirstOrDefaultAsync(t => t.ProgramId == 95 && t.DayNo == ((d - 1) % 7) + 1);

                        bool isWorkout = templateDayRecord != null && !templateDayRecord.DayType.Contains("REST");

                        _context.NtrMealPlanCalendars.Add(new NtrMealPlanCalendar
                        {
                            CycleId = cycleTarget.CycleId,
                            PlanDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(d - 1)),
                            WeekNo = ((d - 1) / 7) + 1,
                            DayNo = d,
                            TemplateId = safeTemplateId, // 🔥 Importante 'to para sa Foreign Key!
                            IsWorkoutDay = isWorkout,
                            Status = "PENDING",
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                    await _context.SaveChangesAsync();

                    // --- 7. MACRO-BALANCED MEAL SEEDING ---
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
                            MealType   = mealCode,
                            Calories   = totals.cal,
                            ProteinG   = totals.prot,
                            CarbsG     = totals.carb,
                            FatsG      = totals.fats
                        });
                    }

                    await _context.SaveChangesAsync();


                    // --- 5. WORKOUT ACTIVATION (SEEDING SESSIONS) ---
                    if (request.SelectedPrograms != null && request.SelectedPrograms.Any())
                    {
                                                // ========================================================================
                        // FIX (Item 12 - Patch 12.2): I-deactivate ang lumang ACTIVE programs bago gumawa ng bago
                        // ========================================================================
                        var oldActivePrograms = await _context.UsrUserProgramInstances
                            .Where(p => p.UserId == userId && p.Status == "ACTIVE")
                            .ToListAsync();

                        if (oldActivePrograms.Any())
                        {
                            foreach (var oldProgram in oldActivePrograms)
                            {
                                oldProgram.Status = "REPLACED"; 
                            }
                            
                            // I-save agad para ma-apply bago mag-create ng bago (nasa loob na ng transaction, so safe)
                            await _context.SaveChangesAsync(); 
                            _logger.LogInformation($"Deactivated {oldActivePrograms.Count} old active program(s) for user {userId} due to re-onboarding.");
                        }

                        // 1. Kunin ang Data mula sa Onboarding Details ni User
                        var onboarding = await _context.UsrUserOnboardingDetails
                            .FirstOrDefaultAsync(o => o.UserId == userId);

                        // 2. Kunin ang Calorie Target mula sa Metrics (na-save na natin kanina sa Nutrition side)
                        var userMetrics = await _context.UsrUserMetrics
                            .OrderByDescending(m => m.RecordedAt)
                            .FirstOrDefaultAsync(m => m.UserId == userId);

                        double dailyTarget = userMetrics?.CalorieTarget ?? 2000;

                        // Gamitin ang Activity Level galing sa Onboarding table
                        string actLevel = (onboarding?.ActivityLevel ?? "Sedentary").ToLower().Replace(" ", "").Replace("_", "");

                        // 3. DYNAMIC MULTIPLIER (4 Levels Only)
                        double burnMultiplier = actLevel switch
                        {
                            "sedentary" => 0.12,
                            "lightlyactive" => 0.18,
                            "active" => 0.22,
                            "veryactive" => 0.28,
                            _ => 0.15
                        };

                        double targetBurnForSession = dailyTarget * burnMultiplier;

                        foreach (var progDto in request.SelectedPrograms)
                        {
                            var inputName = (progDto.Name ?? "").Trim();

                            // Primary attempt: exact match by name and fitness level
                            var template = await _context.WrkProgramTemplates
                                .FirstOrDefaultAsync(t =>
                                    t.ProgramName.Replace(" ", "").ToLower() == inputName.Replace(" ", "").ToLower() &&
                                    t.FitnessLevel.ToLower() == request.FitnessLevel.ToLower());

                            // FALLBACK 1: Rehab user with "REHAB LEVEL"
                            if (template == null && request.IsRehab)
                            {
                                template = await _context.WrkProgramTemplates
                                    .FirstOrDefaultAsync(t =>
                                        t.ProgramName.Replace(" ", "").ToLower() == inputName.Replace(" ", "").ToLower() &&
                                        t.FitnessLevel.ToLower() == "rehab level");
                            }

                            // FALLBACK 2: Last resort – any rehab program
                            if (template == null && request.IsRehab)
                            {
                                template = await _context.WrkProgramTemplates
                                    .FirstOrDefaultAsync(t => t.ProgramName.Contains("Rehab", StringComparison.OrdinalIgnoreCase));
                            }


                            if (template != null)
                            {

                                // 5a. Create Instance
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
                                _context.UsrUserProgramInstances.Add(instance);
                                await _context.SaveChangesAsync(); // Save to get instance ID

                                // 5b. Get Day Structure (Para malaman kung Workout o Rest ang Day 1)
                                var dayStructure = await _context.WrkProgramTemplateDays
                                    .FirstOrDefaultAsync(d => d.ProgramId == template.ProgramId && d.DayNo == 1);

                                if (dayStructure != null)
                                {
                                    // --- 5c. SEED WORKOUT CALENDAR (Para sa Unified Calendar UI mo) ---
                                    // Binago natin ang pangalan mula 'calendarEntry' -> 'workoutCalendarEntry'
                                    var workoutCalendarEntry = new NtrMealPlanCalendar
                                    {
                                        CycleId = cycleTarget.CycleId,
                                        PlanDate = DateOnly.FromDateTime(DateTime.UtcNow),
                                        WeekNo = 1,
                                        DayNo = 1,
                                        TemplateId = matchedTemplate?.TemplateId ?? 1,
                                        // IsWorkoutDay = dayStructure.DayType == "WORKOUT", 
                                        Status = "PENDING"
                                    };

                                    // 5d. INITIALIZE WORKOUT SESSION (Base sa UsrUserWorkoutSession modelBuilder mo)
                                    var session = new UsrUserWorkoutSession
                                    {
                                        UserId = userId,
                                        ProgramInstanceId = instance.InstanceId, // Eto yung "tali" sa instance
                                        WorkoutDay = 1,
                                        Status = "PENDING",
                                        CreatedAt = DateTime.UtcNow
                                        // TANGGALIN ANG CALORIESBURNED DITO KASI WALA SA TABLE MO!
                                    };
                                    _context.UsrUserWorkoutSessions.Add(session);
                                    await _context.SaveChangesAsync();

                                    var sessionInstanceLink = new UsrUserSessionInstance
                                    {
                                        InstanceId = instance.InstanceId,
                                        MonthNo = 1, // Default values para lumusot
                                        WeekNo = 1,
                                        DayNo = 1,
                                        DayType = dayStructure.DayType ?? "WORKOUT",
                                        Status = "PENDING",
                                        CreatedAt = DateTime.UtcNow
                                    };
                                    _context.UsrUserSessionInstances.Add(sessionInstanceLink);
                                    await _context.SaveChangesAsync();

                                    // 5e. SMART SEEDING (With Strict Level Filter)
                                    
                                    // I-normalize ang requested level (tanggalin ang spaces, gawing lowercase)
                                    string requestedLevel = (request.FitnessLevel ?? "Beginner").Trim().ToLower();

                                    var dayWorkoutsPool = await _context.WrkProgramTemplateDaytypeWorkouts
                                        .Include(dw => dw.Workout)
                                        .Where(dw => dw.ProgramId == template.ProgramId
                                                && dw.DayType == dayStructure.DayType
                                                // DAGDAG NATIN 'TO: Siguraduhin na ang workout difficulty ay match sa request!
                                                && (dw.Workout.DifficultyLevel ?? "Beginner").Trim().ToLower() == requestedLevel)
                                        .ToListAsync();

                                        // SAFETY NET: Kung sakaling may human error sa database (hal. nakalimutan i-tag ang level)
                                        if (!dayWorkoutsPool.Any())
                                        {
                                            _logger.LogWarning($"[SEEDING] Strict filter returned 0. Falling back to all workouts for DayType '{dayStructure.DayType}'.");
                                            dayWorkoutsPool = await _context.WrkProgramTemplateDaytypeWorkouts
                                                .Include(dw => dw.Workout)
                                                .Where(dw => dw.ProgramId == template.ProgramId && dw.DayType == dayStructure.DayType)
                                                .OrderBy(dw => dw.Workout.DifficultyLevel)
                                                .ToListAsync();
                                        }

                                    var shuffledPool = dayWorkoutsPool.OrderBy(x => Guid.NewGuid()).ToList();

                                    double currentTotalBurn = 0;
                                    int workoutCount = 0;

                                    foreach (var dw in shuffledPool)
                                    {
                                        // DAPAT GANITO, BABE:
                                        if (workoutCount >= 8 && currentTotalBurn >= targetBurnForSession)
                                            break;

                                        _context.UsrUserSessionWorkouts.Add(new UsrUserSessionWorkout
                                        {
                                            SessionId = session.SessionId,
                                            WorkoutId = dw.WorkoutId,
                                            Sets = dw.SetsDefault,
                                            Reps = dw.RepsDefault,
                                            OrderNo = workoutCount + 1,
                                            LoadKg = 0
                                        });

                                        currentTotalBurn += dw.Workout?.CaloriesBurned ?? 0;
                                        workoutCount++;
                                    }
                                    await _context.SaveChangesAsync();
                                }
                            }
                        }
                    }

                // --- 6. FINALIZE ---

                if (user != null) user.Status = "ACTIVE";
                await _context.SaveChangesAsync();
                
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
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                throw new Exception($"Error during reset: {ex.Message}");
            }
        });
    }
}