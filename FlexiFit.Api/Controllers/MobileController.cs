using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/mobile")]
    public class MobileController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<MobileController> _logger;  // ✅ ADD THIS

        public MobileController(FlexiFitDbContext context, ILogger<MobileController> logger)  // ✅ ADD 
        {
            _context = context;
            _logger = logger;  // ✅ ADD THIS
        }

        [Authorize]
        [HttpGet("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            // 1. FETCH THE USER OBJECT (Ito ang nawawala kaya ka may CS0103 error)
            var user = await _context.UsrUsers.FindAsync(userId);

            // 2. Fetch other details
            var onboardingDetail = await _context.UsrUserOnboardingDetails
                .FirstOrDefaultAsync(x => x.UserId == userId);

            // MAG-PRINT TAYO SA CONSOLE NG VISUAL STUDIO:
            System.Diagnostics.Debug.WriteLine($"USER ID: {userId}");
            System.Diagnostics.Debug.WriteLine($"ONBOARDING FOUND: {onboardingDetail != null}");

            // 3. LOGIC PARA MAWALA ANG ONBOARDING:
            // Kung ang status sa DB ay "ACTIVE", profileComplete is true na agad.
            bool isProfileComplete = (user?.Status == "ACTIVE") || (onboardingDetail != null);

            var activeSession = await _context.UsrUserWorkoutSessions
            .Where(s => s.UserId == userId && s.Status == "PENDING")
            .OrderByDescending(s => s.CreatedAt) // Use CreatedAt or StartedAt
            .Select(s => new {
                s.SessionId,
                s.WorkoutDay, // Para alam ng App kung Day 1 or Day 2
                s.Status,
            exerciseCount = _context.UsrUserSessionWorkouts.Count(sw => sw.SessionId == s.SessionId)
            })
            .FirstOrDefaultAsync();

            var hasProgram = await _context.UsrUserProgramInstances
                .AnyAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            return Ok(new
            {
                profileComplete = isProfileComplete, // Ito ang nagko-control sa screen
                userId = userId,
                status = new
                {
                    code = user?.Status ?? "NEW",
                    hasActiveProgram = hasProgram,
                    currentWorkoutSession = activeSession
                }
            });
        }

        [Authorize]
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);
            var baseUrl = $"{Request.Scheme}://{Request.Host}";

            // --- 1. DATA PRE-FETCH ---
            var user = await _context.UsrUsers.FindAsync(userId);
            var profile = await _context.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            var metrics = await _context.UsrUserMetrics.Where(m => m.UserId == userId).OrderByDescending(m => m.RecordedAt).FirstOrDefaultAsync();
            var target = await _context.NtrUserCycleTargets.Where(t => t.UserId == userId).OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync();

            var latestVersion = await _context.UsrUserProfileVersions
                .Where(v => v.UserId == userId && v.IsCurrent == true)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

            var activeProgram = await _context.UsrUserProgramInstances
                .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            // --- 2. DASHBOARD BASE MAPPING ---
            var dashboardData = new DashboardResponseDto
            {
                Name = !string.IsNullOrWhiteSpace(profile?.Name) ? profile.Name : (user?.Name ?? "Champion"),
                UserName = !string.IsNullOrWhiteSpace(profile?.Username) ? profile.Username : (user?.Username ?? "user"),
                UserAvatar = (profile != null && !string.IsNullOrWhiteSpace(profile.AvatarUrl))
                    ? (profile.AvatarUrl.StartsWith("http") ? profile.AvatarUrl : $"{baseUrl}/{profile.AvatarUrl.TrimStart('/')}")
                    : $"{baseUrl}/uploads/avatars/default.jpg",
                FitnessLevel = activeProgram?.FitnessLevelAtStart ?? (latestVersion?.FitnessLevelSelected ?? "Beginner"),
                Goal = latestVersion?.GoalSelected ?? "LOSE"
            };

            // --- 3. BMI & WATER ---
            double bmiValue = CalculateBMI(metrics?.CurrentWeightKg, metrics?.CurrentHeightCm);
            dashboardData.BmiData = new BmiDataDto { Value = Math.Round(bmiValue, 1), Status = GetBmiStatus(bmiValue) };

            var waterMl = await _context.NtrWaterLogs
                .Where(w => w.UserId == userId && w.LogDate == todayDateOnly)
                .SumAsync(w => (int?)w.WaterMl) ?? 0;

            // --- 4. NUTRITION ENGINE (Flexible & Clean Version) ---
            // 4.1 Intake (Food Calories)
            var intakeToday = await (from dl in _context.NtrDailyLogs
                                     join dml in _context.NtrDailyMealLogs on dl.DailyLogId equals dml.DailyLogId
                                     where dl.UserId == userId && dl.PlanDate == todayDateOnly
                                     select (double?)dml.Calories).SumAsync() ?? 0;

            // 4.2 Burned (from Activity Summary – already includes workouts)
            var burnedToday = await _context.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
                .Select(a => (double?)a.CaloriesBurned)
                .SumAsync() ?? 0;

            double targetValue = target?.DailyTargetNetCalories ?? 2000;

            dashboardData.Nutrition = new NutritionDataDto
            {
                TargetCalories = (int)targetValue,
                Intake = (int)intakeToday,
                Burned = (int)burnedToday,
                NetCalories = (int)(intakeToday - burnedToday),
                Remaining = (int)(targetValue - (intakeToday - burnedToday)),
                WaterGlasses = waterMl / 250,
                WaterTarget = 8
            };

            // --- 5. WORKOUT SEEDING (Show current day exercises) ---
            if (activeProgram != null)
            {
                int currentDay = activeProgram.CurrentDayNo;                 // 1‑28
                int currentWeek = ((currentDay - 1) / 7) + 1;               // 1‑4
                int templateDayNo = ((currentDay - 1) % 7) + 1;             // 1‑7

                // Get the day type for the current day (use week 1 pattern)
                var dayDef = await _context.WrkProgramTemplateDays
                    .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId
                                           && d.DayNo == templateDayNo
                                           && d.WeekNo == 1);               // pattern repeats, so week 1 is the master

                if (dayDef != null)
                {
                    // Check if today's workout is already completed
                    bool isCompletedToday = await _context.UsrUserWorkoutSessions
                        .AnyAsync(s => s.UserId == userId
                                    && s.ProgramInstanceId == activeProgram.InstanceId
                                    && s.WorkoutDay == currentDay
                                    && s.Status == "Completed");

                    // Fetch the template exercises for the current week and day type
                    dashboardData.UpcomingWorkouts = await (from tw in _context.WrkProgramTemplateDaytypeWorkouts
                                                            join w in _context.WrkWorkouts on tw.WorkoutId equals w.WorkoutId
                                                            where tw.ProgramId == activeProgram.ProgramId
                                                               && tw.WeekNo == currentWeek
                                                               && tw.DayType == dayDef.DayType
                                                            orderby tw.WorkoutOrder
                                                            select new WorkoutExerciseDto
                                                            {
                                                                Id = w.WorkoutId,
                                                                Name = w.WorkoutName,
                                                                ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                                                                    ? BuildWorkoutImageUrl(baseUrl, w.Category, w.ImgFilename)
                                                                    : "",
                                                                MuscleGroup = w.MuscleGroup ?? "Full Body",
                                                                Sets = tw.SetsDefault,               // use template sets
                                                                Reps = tw.RepsDefault,               // use template reps
                                                                DurationMinutes = w.Duration ?? 10,
                                                                Calories = (int)(w.CaloriesBurned ?? 50),
                                                                IsCompleted = isCompletedToday
                                                            }).Take(2).ToListAsync();
                }
            }

            // --- 6. MEAL PREVIEW (from template, not logs) ---
            List<MealGroupDto> todayMeals = new List<MealGroupDto>();

            // Get user's dietary preference
            var nutritionProfile = await _context.NtrUserNutritionProfiles
                .FirstOrDefaultAsync(p => p.UserId == userId);
            string dietaryType = nutritionProfile?.DietaryType ?? "BALANCED";

            // Find meal template for this dietary type
            var mealTemplate = await _context.NtrMealTemplates
                .FirstOrDefaultAsync(t => t.DietaryType == dietaryType)
                ?? await _context.NtrMealTemplates.FirstOrDefaultAsync();

            if (mealTemplate != null && activeProgram != null)
            {
                int currentDay = activeProgram.CurrentDayNo;
                int templateDayNo = ((currentDay - 1) % 7) + 1; // pattern repeats every 7 days

                var templateDay = await _context.NtrTemplateDays
                    .Include(td => td.NtrTemplateDayMeals)
                        .ThenInclude(tdm => tdm.NtrTemplateMealItems)
                            .ThenInclude(tmi => tmi.Food)
                    .FirstOrDefaultAsync(td => td.TemplateId == mealTemplate.TemplateId && td.DayNo == templateDayNo);

                if (templateDay != null)
                {
                    foreach (var tdm in templateDay.NtrTemplateDayMeals.OrderBy(m => GetMealOrder(m.MealType)))
                    {
                        var foodItems = tdm.NtrTemplateMealItems
                        .OrderBy(i => i.SortOrder)
                        .Take(2)
                        .Select(i => new FoodItemDto
                        {
                            FoodId = i.FoodId,
                            Name = i.Food.FoodName,
                            Description = i.Food.Description ?? "",
                            ImageUrl = BuildFoodImageUrl(baseUrl, i.Food.DietaryType ?? "balanced", tdm.MealType, i.Food.ImgFilename),
                            DietaryType = i.Food.DietaryType ?? "balanced",
                            Qty = (double)i.DefaultQty,
                            Unit = i.Food.ServingUnit,
                            Calories = (double)i.Food.Calories,
                            Protein = (double)i.Food.ProteinG,
                            Carbs = (double)i.Food.CarbsG,
                            Fats = (double)i.Food.FatsG
                        })
                        .ToList();

                        todayMeals.Add(new MealGroupDto
                        {
                            TemplateMealId = tdm.TemplateMealId,
                            MealType = tdm.MealType,
                            Status = "PENDING",        // no logs yet
                            FoodItems = foodItems
                        });
                    }
                }
            }

            dashboardData.TodayMeals = todayMeals;

            return Ok(dashboardData);
        }

        private static string BuildWorkoutImageUrl(string baseUrl, string category, string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return $"{baseUrl}/images/workouts/default.png";

            string folder = category?.ToLower() switch
            {
                "muscle_gain" => "muscle_gain",
                "cardio" => "cardio",
                "rehab" => "rehab",
                "warmup" => "warmup",          // if you have warmup folder
                _ => "muscle_gain"             // fallback – adjust as needed
            };

            return $"{baseUrl}/images/workouts/{folder}/{fileName}";
        }

        private static string GetCategoryFolder(string? category)
        {
            if (string.IsNullOrEmpty(category))
                return "general";

            return category.ToLower() switch
            {
                "cardio" => "cardio",
                "strength" => "strength",
                "hiit" => "hiit",
                "yoga" => "yoga",
                "warmup" => "warmups",
                "push" => "push",
                "pull" => "pull",
                "legs" => "legs",
                "core" => "core",
                _ => "general"
            };
        }

        [Authorize]
        [HttpPost("onboarding/profile")]
        public async Task<IActionResult> SubmitOnboardingProfile([FromBody] OnboardingProfileRequest request)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {

                // --- 1. BASE PROFILE & USER SYNC (SMART FALLBACK) ---
                var user = await _context.UsrUsers.FindAsync(userId.Value);
                var profile = await _context.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId.Value)
                              ?? new UsrUserProfile { UserId = userId.Value, CreatedAt = DateTime.UtcNow };

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
                    _context.Entry(user).State = EntityState.Modified;

                    // B. UPDATE PROFILE TABLE (PARA SA DASHBOARD DISPLAY)
                    // DITO ANG MAGIC: Kung walang input sa onboarding, hiramin si "cj" at "cy" sa User table.
                    // PALITAN ITO — Hiramin LAGI sa usr_users table, hindi sa request
                    profile.Name = user.Name;      // ← Galing sa usr_users, hindi sa request
                    profile.Username = user.Username;  // ← Galing sa usr_users, hindi sa request
                }

                // C. GENDER & AVATAR
                profile.Gender = request.Gender;

                // Siguradong may default avatar link kung wala pang upload
                if (string.IsNullOrWhiteSpace(profile.AvatarUrl))
                {
                    profile.AvatarUrl = "uploads/avatars/default.jpg";
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
                var oldVersions = await _context.UsrUserProfileVersions.Where(v => v.UserId == userId.Value).ToListAsync();
                foreach (var v in oldVersions) v.IsCurrent = false;

                var profileVersion = new UsrUserProfileVersion
                {
                    UserId = userId.Value,
                    FitnessLevelSelected = request.FitnessLevel ?? "Beginner",
                    GoalSelected = request.BodyGoal ?? "Strength",
                    IsCurrent = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.UsrUserProfileVersions.Add(profileVersion);

                await _context.SaveChangesAsync();


                    // nagana na ng maayos
                    // 3. ONBOARDING DETAILS
                    var details = await _context.UsrUserOnboardingDetails.FirstOrDefaultAsync(d => d.UserId == userId.Value)
                                 ?? new UsrUserOnboardingDetail { UserId = userId.Value, CreatedAt = DateTime.UtcNow };

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


                // --- 4. NUTRITION ENGINE (SEEDING INITIAL MEALS) ---
                double w = (double)request.WeightKg;
                double h = (double)request.HeightCm;
                int age = request.Age > 0 ? request.Age : 25;

                // BMR Calculation
                double bmr = (request.Gender?.ToUpper() == "MALE")
                    ? (10 * w) + (6.25 * h) - (5 * age) + 5
                    : (10 * w) + (6.25 * h) - (5 * age) - 161;

                // 1. DYNAMIC MULTIPLIER (All Caps para consistent)
                string dbActivityLevel = (request.ActivityLevel ?? "SEDENTARY");
                string nutactLevel = (request.ActivityLevel ?? "SEDENTARY").ToUpper().Replace(" ", "").Replace("_", "");
                double multiplier = nutactLevel switch
                {
                    "SEDENTARY" => 1.2,
                    "LIGHTLYACTIVE" => 1.375,
                    "MODERATELYACTIVE" => 1.55,
                    "ACTIVE" => 1.55,
                    "VERYACTIVE" => 1.725,
                    _ => 1.375
                };

                double tdee = bmr * multiplier;
                double calorieTarget = tdee;

                // 2. GOAL ADJUSTMENT
                string goal = (request.BodyGoal ?? "").ToUpper();
                if (goal.Contains("LOSE")) calorieTarget -= 500;
                else if (goal.Contains("GAIN")) calorieTarget += 300;

                // 3. MACRO CALCULATION
                decimal proteinTarget = (decimal)(w * 2.0);
                decimal fatsTarget = (decimal)((calorieTarget * 0.25) / 9);
                decimal carbsTarget = (decimal)((calorieTarget - ((double)proteinTarget * 4) - ((double)fatsTarget * 9)) / 4);

                // A. Add Metrics
                var metrics = new UsrUserMetric
                {
                    UserId = userId.Value,
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


                // ✅ DITO ILAGAY — BAGO ANG "B. Add Cycle Target"
                // C. Save/Update Nutrition Profile
                string dbNutritionGoal = (request.BodyGoal ?? "MAINTAIN");
                var existingNutProfile = await _context.NtrUserNutritionProfiles
                    .FirstOrDefaultAsync(p => p.UserId == userId.Value);


                if (existingNutProfile == null)
                {
                    _context.NtrUserNutritionProfiles.Add(new NtrUserNutritionProfile
                    {
                        UserId = userId.Value,
                        Age = (short)request.Age,
                        HeightCm = request.HeightCm,
                        WeightKg = request.WeightKg,
                        TargetWeightKg = request.TargetWeightKg > 0
                                            ? request.TargetWeightKg
                                            : request.WeightKg,
                        NutritionGoal = dbNutritionGoal,   // NOT NULL sa DB
                        ActivityLevel = dbActivityLevel,    // NOT NULL sa DB
                        DietaryType = request.DietType ?? "BALANCED",
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    existingNutProfile.Age = (short)request.Age;
                    existingNutProfile.HeightCm = request.HeightCm;
                    existingNutProfile.WeightKg = request.WeightKg;
                    if (request.TargetWeightKg > 0)
                        existingNutProfile.TargetWeightKg = request.TargetWeightKg;
                    existingNutProfile.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();


                // B. Add Cycle Target
                var cycleTarget = new NtrUserCycleTarget
                {
                    UserId = userId.Value,
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

                // --- 5. CREATE DAILY LOG (Para sa Dashboard Today) ---
                var dailyLog = new NtrDailyLog
                {
                    UserId = userId.Value,
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

                // --- 7. NESTED SEEDING: FOOD ITEMS (Para may pagkain agad Dashboard) ---
                // REMOVED – meal logs are now created only when the user completes the nutrition day.



                // --- 5. WORKOUT ACTIVATION (SEEDING SESSIONS) ---
                if (request.SelectedPrograms != null && request.SelectedPrograms.Any())
                {
                    // 1. Kunin ang Data mula sa Onboarding Details ni User
                    var onboarding = await _context.UsrUserOnboardingDetails
                        .FirstOrDefaultAsync(o => o.UserId == userId.Value);

                    // 2. Kunin ang Calorie Target mula sa Metrics
                    var userMetrics = await _context.UsrUserMetrics
                        .OrderByDescending(m => m.RecordedAt)
                        .FirstOrDefaultAsync(m => m.UserId == userId.Value);

                    double dailyTarget = userMetrics?.CalorieTarget ?? 2000;

                    // Gamitin ang Activity Level galing sa Onboarding table
                    string actLevel = (onboarding?.ActivityLevel ?? "Sedentary").ToLower().Replace(" ", "").Replace("_", "");

                    // 3. DYNAMIC MULTIPLIER
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
                        var template = await _context.WrkProgramTemplates
                            .FirstOrDefaultAsync(t =>
                                t.ProgramName.Replace(" ", "").ToLower() == inputName.Replace(" ", "").ToLower() &&
                                t.FitnessLevel.ToLower() == request.FitnessLevel.ToLower()
                            );

                        if (template != null)
                        {
                            // 5a. Create Instance
                            var instance = new UsrUserProgramInstance
                            {
                                UserId = userId.Value,
                                ProgramId = template.ProgramId,
                                ProfileVersionId = profileVersion.ProfileVersionId,
                                CycleNo = cycleTarget.CycleId,
                                Status = "ACTIVE",
                                CurrentDayNo = 1,
                                FitnessLevelAtStart = request.FitnessLevel,
                                CreatedAt = DateTime.UtcNow
                            };
                            _context.UsrUserProgramInstances.Add(instance);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Created cycle target with ID: {cycleTarget.CycleId}"); // ✅ ADD THIS

                            // 5b. Get Day Structure
                            var dayStructure = await _context.WrkProgramTemplateDays
                                .FirstOrDefaultAsync(d => d.ProgramId == template.ProgramId && d.DayNo == 1);

                            if (dayStructure != null && !dayStructure.DayType.Contains("REST"))
                            {
                                // 5d. INITIALIZE WORKOUT SESSION
                                var session = new UsrUserWorkoutSession
                                {
                                    UserId = userId.Value,
                                    ProgramInstanceId = instance.InstanceId,
                                    WorkoutDay = 1,
                                    Status = "PENDING",
                                    StartedAt = DateTime.UtcNow,
                                    CreatedAt = DateTime.UtcNow
                                };
                                _context.UsrUserWorkoutSessions.Add(session);
                                await _context.SaveChangesAsync();

                                var sessionInstanceLink = new UsrUserSessionInstance
                                {
                                    InstanceId = instance.InstanceId,
                                    MonthNo = 1,
                                    WeekNo = 1,
                                    DayNo = 1,
                                    DayType = dayStructure.DayType ?? "WORKOUT",
                                    Status = "PENDING",
                                    CreatedAt = DateTime.UtcNow
                                };
                                _context.UsrUserSessionInstances.Add(sessionInstanceLink);
                                await _context.SaveChangesAsync();

                                // ✅ GET WARMUPS (2 random)
                                var warmups = await _context.WrkWorkouts
                                    .Where(w => w.Category != null && w.Category.ToUpper() == "WARMUP" && w.IsActive == true)
                                    .OrderBy(w => Guid.NewGuid())
                                    .Take(2)
                                    .ToListAsync();

                                _logger.LogInformation($"Adding {warmups.Count} warmups to session {session.SessionId}");

                                // ✅ GET MAIN WORKOUTS
                                var dayWorkoutsPool = await _context.WrkProgramTemplateDaytypeWorkouts
                                    .Include(dw => dw.Workout)
                                    .Where(dw => dw.ProgramId == template.ProgramId
                                              && dw.DayType == dayStructure.DayType
                                              && dw.Workout.DifficultyLevel == request.FitnessLevel)
                                    .ToListAsync();

                                var shuffledPool = dayWorkoutsPool.OrderBy(x => Guid.NewGuid()).ToList();

                                double currentTotalBurn = 0;
                                int workoutOrder = 1;  // ✅ FIXED: Changed from 'order' to 'workoutOrder'

                                // ✅ ADD WARMUPS FIRST
                                foreach (var warmup in warmups)
                                {
                                    _context.UsrUserSessionWorkouts.Add(new UsrUserSessionWorkout
                                    {
                                        SessionId = session.SessionId,
                                        WorkoutId = warmup.WorkoutId,
                                        Sets = 2,
                                        Reps = 12,
                                        OrderNo = workoutOrder++,  // ✅ FIXED: using workoutOrder
                                        LoadKg = 0
                                    });
                                }

                                // ✅ ADD MAIN WORKOUTS
                                foreach (var dw in shuffledPool)
                                {
                                    if (workoutOrder > 10 && currentTotalBurn >= targetBurnForSession) // Max 10 total
                                        break;

                                    _context.UsrUserSessionWorkouts.Add(new UsrUserSessionWorkout
                                    {
                                        SessionId = session.SessionId,
                                        WorkoutId = dw.WorkoutId,
                                        Sets = dw.SetsDefault,
                                        Reps = dw.RepsDefault,
                                        OrderNo = workoutOrder++,  // ✅ FIXED: using workoutOrder
                                        LoadKg = 0
                                    });

                                    currentTotalBurn += dw.Workout?.CaloriesBurned ?? 0;
                                }

                                await _context.SaveChangesAsync();
                                _logger.LogInformation($"Created session {session.SessionId} with {workoutOrder - 1} exercises (Warmups: {warmups.Count})");
                            }
                        }
                    }
                }

                // --- 6. FINALIZE ---

                if (user != null) user.Status = "ACTIVE";
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { message = "Onboarding complete! Dashboard initialized.", status = "ACTIVE" });
            }
            catch (Exception ex)
            {
                // ETO ANG SIKRETO, BABE!
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, $"Onboarding failed: {innerMessage}");
            }
        }

        [Authorize]
        [HttpDelete("debug/reset-onboarding/{userId}")]
        public async Task<IActionResult> ResetOnboarding(int userId)
        {
            var currentUserId = GetUserId();
            if (currentUserId != userId) return Forbid();

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // --- 1. WORKOUTS ---
                var sessions = _context.UsrUserWorkoutSessions.Where(x => x.UserId == userId);
                var sessionIds = await sessions.Select(s => s.SessionId).ToListAsync();

                await _context.UsrUserSessionWorkouts.Where(x => sessionIds.Contains(x.SessionId)).ExecuteDeleteAsync();
                await _context.UsrUserSessionInstances.Where(x => sessionIds.Contains(x.SessionId)).ExecuteDeleteAsync();
                await _context.UsrUserWorkoutProgresses.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserWorkoutSessions.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserProgramInstances.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // --- 2. NUTRITION ---
                // Delete meal item logs, meal logs, daily logs, water logs
                await _context.NtrDailyMealItemLogs.Where(x => x.DailyLog.UserId == userId).ExecuteDeleteAsync();
                await _context.NtrDailyMealLogs.Where(x => x.DailyLog.UserId == userId).ExecuteDeleteAsync();
                await _context.NtrDailyLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.NtrWaterLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // Delete cycle targets (cascade will delete meal plan calendars)
                var cycles = _context.NtrUserCycleTargets.Where(x => x.UserId == userId);
                _context.NtrUserCycleTargets.RemoveRange(cycles);

                // Delete nutrition profile (dietary preferences)
                await _context.NtrUserNutritionProfiles.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // --- 3. ACTIVITY SUMMARY (calories burned, minutes) ---
                await _context.ActActivitySummaries.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // --- 4. PROFILE & METRICS ---
                await _context.UsrUserMetrics.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserOnboardingDetails.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserProfileVersions.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserProfiles.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // --- 5. NOTIFICATION SETTINGS ---
                await _context.UsrUserNotificationSettings.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // Save tracked deletions (e.g., cycles)
                await _context.SaveChangesAsync();

                // --- 6. RESET USER STATUS ---
                _context.ChangeTracker.Clear();

                var userToReset = await _context.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
                if (userToReset != null)
                {
                    userToReset.Status = "PENDING_ONBOARDING";
                    userToReset.UpdatedAt = DateTime.UtcNow;
                    _context.UsrUsers.Update(userToReset);
                }

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { message = "Complete user data reset successfully." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new
                {
                    message = "Error during reset. Rollback completed.",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }


        #region Helpers
        private int? GetUserId()
        {
            var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }

        private int GetMealOrder(string mealType) => mealType switch { "B" => 1, "L" => 2, "S" => 3, "D" => 4, _ => 5 };
        private string BuildFoodImageUrl(string baseUrl, string category, string mealType, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return $"{baseUrl}/images/foods/default.png";
            string typeFolder = mealType.ToUpper() switch { "B" => "breakfast", "L" => "lunch", "S" => "snacks", "D" => "dinner", _ => "general" };
            return $"{baseUrl}/images/foods/{typeFolder}/{fileName}";
        }

        private static string MapNutritionGoal(string bodyGoal)
        {
            return bodyGoal?.Trim().ToUpper() switch { "LOSE" => "LOSE", "GAIN" => "GAIN", _ => "MAINTAIN" };
        }

        private double CalculateBMI(decimal? weight, decimal? height)
        {
            if (weight == null || height == null || height <= 0) return 0;
            var hMeters = (double)height / 100;
            return Math.Round((double)weight / (hMeters * hMeters), 1);
        }

        private string GetBmiStatus(double bmi)
        {
            if (bmi <= 0) return "No data";
            if (bmi < 18.5) return "Underweight";
            if (bmi < 25) return "Normal weight";
            if (bmi < 30) return "Overweight";
            return "Obese";
        }
        #endregion
    }
}