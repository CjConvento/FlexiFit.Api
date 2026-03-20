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

        public MobileController(FlexiFitDbContext context)
        {
            _context = context;
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

            // 4.2 Burned Manual (Activities)
            var burnedManualValue = await _context.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
                .Select(a => (double?)a.CaloriesBurned)
                .SumAsync() ?? 0;

            // 4.3 Burned Workouts (Join to get exercise calories)
            var burnedWorkoutsValue = await (from s in _context.UsrUserWorkoutSessions
                                             join usw in _context.UsrUserSessionWorkouts on s.SessionId equals usw.SessionId
                                             join w in _context.WrkWorkouts on usw.WorkoutId equals w.WorkoutId
                                             where s.UserId == userId && s.Status == "Completed"
                                             && s.CreatedAt >= DateTime.Today
                                             select (double?)w.CaloriesBurned).SumAsync() ?? 0;

            double totalBurnedSum = burnedManualValue + burnedWorkoutsValue;
            double targetValue = target?.DailyTargetNetCalories ?? 2000;

            dashboardData.Nutrition = new NutritionDataDto
            {
                TargetCalories = (int)targetValue,
                Intake = (int)intakeToday,
                Burned = (int)totalBurnedSum,
                NetCalories = (int)(intakeToday - totalBurnedSum),
                Remaining = (int)(targetValue - (intakeToday - totalBurnedSum)),
                WaterGlasses = waterMl / 250,
                WaterTarget = 8
            };

            // --- 5. WORKOUT SEEDING (Show current day exercises) ---
            if (activeProgram != null)
            {
                var dayDef = await _context.WrkProgramTemplateDays
                    .FirstOrDefaultAsync(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == activeProgram.CurrentDayNo);

                if (dayDef != null)
                {
                    // Check if user already finished today's session
                    bool isCompletedToday = await _context.UsrUserWorkoutSessions
                        .AnyAsync(s => s.UserId == userId && s.ProgramInstanceId == activeProgram.InstanceId
                                  && s.WorkoutDay == activeProgram.CurrentDayNo && s.Status == "Completed");

                    dashboardData.UpcomingWorkouts = await (from tw in _context.WrkProgramTemplateDaytypeWorkouts
                                                            join w in _context.WrkWorkouts on tw.WorkoutId equals w.WorkoutId
                                                            where tw.ProgramId == activeProgram.ProgramId && tw.DayType == dayDef.DayType
                                                            orderby tw.WorkoutId
                                                            select new WorkoutExerciseDto
                                                            {
                                                                Id = w.WorkoutId,
                                                                Name = w.WorkoutName,
                                                                ImageFileName = !string.IsNullOrEmpty(w.ImgFilename)
                                                                    ? $"{baseUrl}/images/workouts/{w.Category.ToLower()}/{w.ImgFilename}" : "",
                                                                MuscleGroup = w.MuscleGroup ?? "Full Body",
                                                                Sets = 3,
                                                                Reps = 12,
                                                                DurationMinutes = w.Duration ?? 10,
                                                                Calories = (int)(w.CaloriesBurned ?? 50),
                                                                IsCompleted = isCompletedToday
                                                            }).Take(2).ToListAsync();
                }
            }

            // --- 6. MEAL PREVIEW (Fetch Daily Logs & Food Items) ---
            var rawMealData = await (from cm in _context.NtrMealPlanCalendars
                                     join dl in _context.NtrDailyLogs
                                        on new { cm.CycleId, Date = cm.PlanDate }
                                        equals new { dl.CycleId, Date = dl.PlanDate }
                                     join dml in _context.NtrDailyMealLogs on dl.DailyLogId equals dml.DailyLogId
                                     join dmil in _context.NtrDailyMealItemLogs
                                        on new { dml.DailyLogId, dml.MealType }
                                        equals new { dmil.DailyLogId, dmil.MealType }
                                     join food in _context.NtrFoodItems on dmil.FoodId equals food.FoodId
                                     where cm.Cycle.UserId == userId
                                        && cm.PlanDate == todayDateOnly
                                     select new
                                     {
                                         MealType = dml.MealType,
                                         DayStatus = cm.Status, // Sync with calendar status
                                         Food = new FoodItemDto
                                         {
                                             FoodId = food.FoodId,
                                             Name = food.FoodName,
                                             Description = food.Description ?? "",
                                             ImageUrl = !string.IsNullOrEmpty(food.ImgFilename)
                                                 ? $"{baseUrl}/images/foods/balanced/{(dml.MealType == "B" ? "breakfast" : dml.MealType == "L" ? "lunch" : dml.MealType == "D" ? "dinner" : "snacks")}/{food.ImgFilename}"
                                                 : $"{baseUrl}/images/foods/default.png",
                                             Qty = (double)dmil.Qty,
                                             Unit = food.ServingUnit,
                                             Calories = (double)dmil.Calories,
                                             Protein = (double)dmil.ProteinG,
                                             Carbs = (double)dmil.CarbsG,
                                             Fats = (double)dmil.FatsG
                                         }
                                     }).ToListAsync();

            // --- 7. MEMORY GROUPING (Group by Meal Type) ---
            dashboardData.TodayMeals = rawMealData
                .GroupBy(x => x.MealType)
                .Select(g => new MealGroupDto
                {
                    TemplateMealId = g.Key == "B" ? 1 : g.Key == "L" ? 2 : g.Key == "S" ? 3 : 4,
                    MealType = g.Key ?? "B",
                    Status = g.First().DayStatus ?? "PENDING",
                    FoodItems = g.Select(x => x.Food)
                                 .GroupBy(f => f.FoodId)
                                 .Select(group => group.First())
                                 .Take(2)
                                 .ToList()
                })
                .OrderBy(m => m.MealType == "B" ? 1 : m.MealType == "L" ? 2 : m.MealType == "S" ? 3 : 4)
                .ToList();

            return Ok(dashboardData);
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
                        // 🔥 ETO ANG FIX: Dapat match ang Pangalan AT ang Fitness Level
                        var template = await _context.WrkProgramTemplates
                            .FirstOrDefaultAsync(t =>
                                t.ProgramName.Replace(" ", "").ToLower() == inputName.Replace(" ", "").ToLower() &&
                                t.FitnessLevel.ToLower() == request.FitnessLevel.ToLower() // Isama natin si Level!
                            );

                        if (template != null)
                        {
                            // 5a. Create Instance
                            var instance = new UsrUserProgramInstance
                            {
                                UserId = userId.Value,
                                ProgramId = template.ProgramId,
                                ProfileVersionId = profileVersion.ProfileVersionId,
                                CycleNo = 1,
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
                // --- 1. WORKOUTS (DIRECT DELETE) ---
                // Ginagamit natin ang ExecuteDeleteAsync para i-bypass ang tracking conflicts
                var sessions = _context.UsrUserWorkoutSessions.Where(x => x.UserId == userId);
                var sessionIds = await sessions.Select(s => s.SessionId).ToListAsync();

                await _context.UsrUserSessionWorkouts.Where(x => sessionIds.Contains(x.SessionId)).ExecuteDeleteAsync();
                await _context.UsrUserSessionInstances.Where(x => sessionIds.Contains(x.SessionId)).ExecuteDeleteAsync();
                await _context.UsrUserWorkoutProgresses.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserWorkoutSessions.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserProgramInstances.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // --- 2. NUTRITION (CASCADE & DIRECT DELETE) ---

                // 2a. Direct Delete sa logs para siguradong malinis ang Unique Constraints
                // Inuuna ang Item Logs bago ang Daily Logs
                await _context.NtrDailyMealItemLogs.Where(x => x.DailyLog.UserId == userId).ExecuteDeleteAsync();
                await _context.NtrDailyMealLogs.Where(x => x.DailyLog.UserId == userId).ExecuteDeleteAsync();
                await _context.NtrDailyLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.NtrWaterLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // 2b. Burahin ang Cycle Targets
                // NOTE: Dahil sa 'ON DELETE CASCADE' sa SQL Schema mo, kusa na ring
                // mabubura nito ang related NtrMealPlanCalendars kung hindi pa sila nabura.
                var cycles = _context.NtrUserCycleTargets.Where(x => x.UserId == userId);
                _context.NtrUserCycleTargets.RemoveRange(cycles);

                // --- 3. PROFILE & METRICS (DIRECT DELETE) ---
                await _context.UsrUserMetrics.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserOnboardingDetails.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserProfileVersions.Where(x => x.UserId == userId).ExecuteDeleteAsync();
                await _context.UsrUserProfiles.Where(x => x.UserId == userId).ExecuteDeleteAsync();

                // STEP A: I-save ang tracked deletions (yung cycles)
                await _context.SaveChangesAsync();

                // --- 4. RESET USER STATUS ---
                // STEP B: Clear tracker para siguradong fresh ang version ng User record
                _context.ChangeTracker.Clear();

                var userToReset = await _context.UsrUsers.FirstOrDefaultAsync(u => u.UserId == userId);
                if (userToReset != null)
                {
                    userToReset.Status = "PENDING_ONBOARDING";
                    userToReset.UpdatedAt = DateTime.UtcNow;
                    _context.UsrUsers.Update(userToReset);
                }

                // STEP C: Final Save and Commit
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { message = "Gudis na babe! Deep clean na ang user data at reset na ang status." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new
                {
                    message = "Error during reset, babe. Nag-rollback tayo para safe.",
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