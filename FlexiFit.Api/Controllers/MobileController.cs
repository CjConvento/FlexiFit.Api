using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Client;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/mobile")]
    public class MobileController : ControllerBase
    {
        private readonly IDbContextFactory<FlexiFitDbContext> _contextFactory;
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<MobileController> _logger;  // ✅ ADD THIS
        private readonly IMemoryCache _cache;   // <-- add this field

        public MobileController(
            FlexiFitDbContext context,
            IDbContextFactory<FlexiFitDbContext> contextFactory,
            ILogger<MobileController> logger,
            IMemoryCache memoryCache)
        {
            _context = context;
            _contextFactory = contextFactory; 
            _logger = logger;
            _cache = memoryCache;
        }

        [Authorize]
        [HttpGet("bootstrap")]
        public async Task<IActionResult> Bootstrap()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _context.UsrUsers.FindAsync(userId);
            var onboardingDetail = await _context.UsrUserOnboardingDetails
                .FirstOrDefaultAsync(x => x.UserId == userId);

            // Debug output
            System.Diagnostics.Debug.WriteLine($"USER ID: {userId}");
            System.Diagnostics.Debug.WriteLine($"ONBOARDING FOUND: {onboardingDetail != null}");

            // Determine if onboarding is complete:
            // The user is considered fully onboarded only when their status is "ACTIVE".
            bool isProfileComplete = user?.Status == "ACTIVE";

            // Optional: If you want to ensure all required fields are filled even when status is not ACTIVE,
            // you could check that the onboarding detail record is fully populated.
            // For now, we rely solely on the user's status.

            var activeSession = await _context.UsrUserWorkoutSessions
                .Where(s => s.UserId == userId && s.Status == "PENDING")
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new {
                    s.SessionId,
                    s.WorkoutDay,
                    s.Status,
                    exerciseCount = _context.UsrUserSessionWorkouts.Count(sw => sw.SessionId == s.SessionId)
                })
                .FirstOrDefaultAsync();

            var hasProgram = await _context.UsrUserProgramInstances
                .AnyAsync(p => p.UserId == userId && p.Status == "ACTIVE");

            return Ok(new
            {
                profileComplete = isProfileComplete,
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

            try
            {
                // 1. FETCH USER DATA (sequential)
                var user = await _context.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
                var profile = await _context.UsrUserProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
                var metrics = await _context.UsrUserMetrics
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.RecordedAt)
                    .FirstOrDefaultAsync();
                var target = await _context.NtrUserCycleTargets
                    .Where(t => t.UserId == userId)
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefaultAsync();
                var latestVersion = await _context.UsrUserProfileVersions
                    .Where(v => v.UserId == userId && v.IsCurrent == true)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefaultAsync();
                var activeProgram = await _context.UsrUserProgramInstances
                    .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");

                // 2. BUILD BASE DASHBOARD
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

                // 3. BMI
                double bmiValue = CalculateBMI(metrics?.CurrentWeightKg, metrics?.CurrentHeightCm);
                dashboardData.BmiData = new BmiDataDto { Value = Math.Round(bmiValue, 1), Status = GetBmiStatus(bmiValue) };

                // 4. Get today's daily log (created during onboarding)
                var todayLog = await _context.NtrDailyLogs
                    .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

                // Determine water consumption (still from water logs)
                var waterMl = await _context.NtrWaterLogs
                    .Where(w => w.UserId == userId && w.LogDate == todayDateOnly)
                    .SumAsync(w => (int?)w.WaterMl) ?? 0;

                dashboardData.Nutrition = new NutritionDataDto
                {
                    TargetCalories = todayLog?.TargetNetCalories,                    // int?
                    Intake = todayLog?.CaloriesConsumed,                             // int?
                    Burned = (double)(todayLog?.CaloriesBurned ?? 0),                // double, default 0
                    NetCalories = todayLog?.NetCalories,                             // int?
                    Remaining = todayLog != null ? todayLog.TargetNetCalories - todayLog.NetCalories : (int?)null,
                    WaterGlasses = waterMl / 250,
                    WaterTarget = 8                                                  // optional, can be null if not set
                };

                // 5. WORKOUT DATA
                if (activeProgram != null)
                {
                    int currentDay = activeProgram.CurrentDayNo;
                    int currentWeek = ((currentDay - 1) / 7) + 1;
                    int templateDayNo = ((currentDay - 1) % 7) + 1;

                    var dayDef = await _context.WrkProgramTemplateDays
                        .Where(d => d.ProgramId == activeProgram.ProgramId
                                 && d.DayNo == templateDayNo
                                 && d.WeekNo == 1)
                        .Select(d => d.DayType)
                        .FirstOrDefaultAsync();

                    if (dayDef != null)
                    {
                        bool isCompletedToday = await _context.UsrUserWorkoutSessions
                            .AnyAsync(s => s.UserId == userId
                                        && s.ProgramInstanceId == activeProgram.InstanceId
                                        && s.WorkoutDay == currentDay
                                        && s.Status == "Completed");

                        // Fetch raw workout data
                        var rawWorkouts = await (from tw in _context.WrkProgramTemplateDaytypeWorkouts
                                                 join w in _context.WrkWorkouts on tw.WorkoutId equals w.WorkoutId
                                                 where tw.ProgramId == activeProgram.ProgramId
                                                    && tw.WeekNo == currentWeek
                                                    && tw.DayType == dayDef
                                                 orderby tw.WorkoutOrder
                                                 select new
                                                 {
                                                     w.WorkoutId,
                                                     w.WorkoutName,
                                                     w.ImgFilename,
                                                     w.Category,
                                                     w.MuscleGroup,
                                                     w.Duration,
                                                     w.CaloriesBurned,
                                                     tw.SetsDefault,
                                                     tw.RepsDefault
                                                 })
                                                 .Take(2)
                                                 .ToListAsync();

                        // Build DTOs in memory
                        var workouts = rawWorkouts.Select(w => new WorkoutExerciseDto
                        {
                            Id = w.WorkoutId,
                            Name = w.WorkoutName,
                            ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                                ? BuildWorkoutImageUrl(baseUrl, w.Category, w.ImgFilename)
                                : "",
                            MuscleGroup = w.MuscleGroup ?? "Full Body",
                            Sets = w.SetsDefault,
                            Reps = w.RepsDefault,
                            DurationMinutes = w.Duration ?? 10,
                            Calories = (int)(w.CaloriesBurned ?? 50),
                            IsCompleted = isCompletedToday
                        }).ToList();

                        dashboardData.UpcomingWorkouts = workouts;
                    }
                }

                // 6. MEAL PREVIEW
                List<MealGroupDto> todayMeals = new List<MealGroupDto>();

                var nutritionProfile = await _context.NtrUserNutritionProfiles
                    .Where(p => p.UserId == userId)
                    .Select(p => p.DietaryType)
                    .FirstOrDefaultAsync();
                string dietaryType = nutritionProfile ?? "BALANCED";

                var mealTemplate = await GetOrCacheMealTemplate(dietaryType);
                if (mealTemplate != null && activeProgram != null)
                {
                    int currentDay = activeProgram.CurrentDayNo;
                    int templateDayNo = ((currentDay - 1) % 7) + 1;

                    // Fetch raw data
                    var rawData = await _context.NtrTemplateDays
                        .Where(td => td.TemplateId == mealTemplate.TemplateId && td.DayNo == templateDayNo)
                        .Select(td => new
                        {
                            td.TemplateDayId,
                            Meals = td.NtrTemplateDayMeals
                                .Select(m => new
                                {
                                    m.TemplateMealId,
                                    m.MealType,
                                    FoodItems = m.NtrTemplateMealItems
                                        .OrderBy(i => i.SortOrder)
                                        .Take(2)
                                        .Select(i => new
                                        {
                                            i.FoodId,
                                            i.Food.FoodName,
                                            i.Food.Description,
                                            i.Food.DietaryType,
                                            i.DefaultQty,
                                            i.Food.ServingUnit,
                                            i.Food.Calories,
                                            i.Food.ProteinG,
                                            i.Food.CarbsG,
                                            i.Food.FatsG,
                                            i.Food.ImgFilename
                                        }).ToList()
                                }).ToList()
                        })
                        .FirstOrDefaultAsync();

                    if (rawData != null)
                    {
                        // Build DTOs in memory
                        todayMeals = rawData.Meals
                            .OrderBy(m => GetMealOrder(m.MealType))
                            .Select(m => new MealGroupDto
                            {
                                TemplateMealId = m.TemplateMealId,
                                MealType = m.MealType,
                                Status = "PENDING",
                                FoodItems = m.FoodItems.Select(fi => new FoodItemDto
                                {
                                    FoodId = fi.FoodId,
                                    Name = fi.FoodName,
                                    Description = fi.Description ?? "",
                                    ImageUrl = BuildFoodImageUrl(baseUrl, fi.DietaryType ?? "balanced", m.MealType, fi.ImgFilename),
                                    DietaryType = fi.DietaryType ?? "balanced",
                                    Qty = (double)fi.DefaultQty,
                                    Unit = fi.ServingUnit,
                                    Calories = (double)fi.Calories,
                                    Protein = (double)fi.ProteinG,
                                    Carbs = (double)fi.CarbsG,
                                    Fats = (double)fi.FatsG
                                }).ToList()
                            }).ToList();
                    }
                }

                dashboardData.TodayMeals = todayMeals;
                _logger.LogInformation("Dashboard data built successfully, returning response.");
                var json = System.Text.Json.JsonSerializer.Serialize(dashboardData);
                _logger.LogInformation($"Dashboard response size: {json.Length} characters");   
                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetDashboard for user {UserId}", userId);
                return StatusCode(500, "An error occurred while fetching dashboard data.");
            }
        }

        // Helper for caching (inject IMemoryCache)
        private async Task<NtrMealTemplate> GetOrCacheMealTemplate(string dietaryType)
        {
            var cacheKey = $"MealTemplate_{dietaryType}";
            if (!_cache.TryGetValue(cacheKey, out NtrMealTemplate template))
            {
                template = await _context.NtrMealTemplates
                    .Where(t => t.DietaryType == dietaryType)
                    .FirstOrDefaultAsync();

                if (template == null)
                    template = await _context.NtrMealTemplates.FirstOrDefaultAsync();

                // Cache for 10 minutes (adjust as needed)
                _cache.Set(cacheKey, template, TimeSpan.FromMinutes(10));
            }
            return template;
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
                    profile.Name = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : user.Name;
                    profile.Username = !string.IsNullOrWhiteSpace(request.Username) ? request.Username : user.Username;
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

                // --- ADD/UPDATE NUTRITION PROFILE ---
                var nutProfile = await _context.NtrUserNutritionProfiles
                    .FirstOrDefaultAsync(p => p.UserId == userId.Value);
                if (nutProfile == null)
                {
                    nutProfile = new NtrUserNutritionProfile
                    {
                        UserId = userId.Value,
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
                var templateDay = await _context.NtrTemplateDays
                    .Include(td => td.NtrTemplateDayMeals)
                        .ThenInclude(tdm => tdm.NtrTemplateMealItems)
                            .ThenInclude(tmi => tmi.Food)
                    .FirstOrDefaultAsync(td => td.TemplateId == safeTemplateId && td.DayNo == 1);

                if (templateDay != null)
                {
                    foreach (var tdm in templateDay.NtrTemplateDayMeals)
                    {
                        decimal totalCal = 0, totalProt = 0, totalCarbs = 0, totalFats = 0;
                        var itemsToLog = new List<NtrDailyMealItemLog>();

                        foreach (var item in tdm.NtrTemplateMealItems)
                        {
                            if (item.Food == null) continue;

                            var qty = (decimal)item.DefaultQty;
                            var cal = item.Food.Calories * qty;
                            var prot = item.Food.ProteinG * qty;
                            var carb = item.Food.CarbsG * qty;
                            var fat = item.Food.FatsG * qty;

                            itemsToLog.Add(new NtrDailyMealItemLog
                            {
                                DailyLogId = dailyLog.DailyLogId, // ✅ Correctly scoped
                                MealType = tdm.MealType,
                                FoodId = item.FoodId,
                                Qty = item.DefaultQty,
                                IsAddon = item.IsOptionalAddon,
                                Calories = cal,
                                ProteinG = prot,
                                CarbsG = carb,
                                FatsG = fat,
                                SortOrder = item.SortOrder
                            });

                            totalCal += cal;
                            totalProt += prot;
                            totalCarbs += carb;
                            totalFats += fat;
                        }

                        var mealLog = new NtrDailyMealLog
                        {
                            DailyLogId = dailyLog.DailyLogId,
                            MealType = tdm.MealType,
                            Calories = (int)totalCal,
                            ProteinG = totalProt,
                            CarbsG = totalCarbs,
                            FatsG = totalFats
                        };
                        _context.NtrDailyMealLogs.Add(mealLog);
                        _context.NtrDailyMealItemLogs.AddRange(itemsToLog);
                    }
                    await _context.SaveChangesAsync();
                }



                // --- 5. WORKOUT ACTIVATION (SEEDING SESSIONS) ---
                if (request.SelectedPrograms != null && request.SelectedPrograms.Any())
                {
                    // 1. Kunin ang Data mula sa Onboarding Details ni User
                    var onboarding = await _context.UsrUserOnboardingDetails
                        .FirstOrDefaultAsync(o => o.UserId == userId.Value);

                    // 2. Kunin ang Calorie Target mula sa Metrics (na-save na natin kanina sa Nutrition side)
                    var userMetrics = await _context.UsrUserMetrics
                        .OrderByDescending(m => m.RecordedAt)
                        .FirstOrDefaultAsync(m => m.UserId == userId.Value);

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
                                    UserId = userId.Value,
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

                                // 5e. SMART SEEDING
                                // 5e. SMART SEEDING (With Strict Level Filter)
                                var dayWorkoutsPool = await _context.WrkProgramTemplateDaytypeWorkouts
                                    .Include(dw => dw.Workout)
                                    .Where(dw => dw.ProgramId == template.ProgramId
                                              && dw.DayType == dayStructure.DayType
                                              // 🔥 DAGDAG NATIN 'TO: Siguraduhin na ang workout difficulty ay match sa request!
                                              && dw.Workout.DifficultyLevel == request.FitnessLevel)
                                    .ToListAsync();

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