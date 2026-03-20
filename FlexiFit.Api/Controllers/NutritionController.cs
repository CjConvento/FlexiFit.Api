using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/nutrition")]
    public class NutritionController : ControllerBase
    {
        private readonly FlexiFitDbContext _db;
        public NutritionController(FlexiFitDbContext db) => _db = db;

        [HttpGet("today-plan")]
        public async Task<IActionResult> GetTodayPlan()
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            // 1. 🔥 THE FIX: Join sa Cycle at i-check ang status sa Calendar table
            // Base sa screenshot mo, ang 'status' ay nasa ntr_meal_plan_calendar
            var calendarDay = await _db.NtrMealPlanCalendars
                .Include(c => c.Cycle)
                .FirstOrDefaultAsync(c =>
                    c.Cycle.UserId == userId &&
                    c.PlanDate == todayDateOnly &&
                    c.Status == "PENDING"); // 💡 O gamitin ang 'ACTIVE' kung binabago mo ito

            if (calendarDay == null) return NotFound(new { message = "No active meal plan found for today." });

            // 2. Kunin ang latest user targets
            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefaultAsync();

            // 3. Engine: Calories Burned
            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly)
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            // 4. 🔥 REVISED QUERY: Fetch Meals and Top 2 Food Items
            var mealGroupsRaw = await _db.NtrTemplateDayMeals
                .Where(tdm => tdm.TemplateDayId == calendarDay.TemplateId)
                .Select(tdm => new
                {
                    tdm.TemplateMealId,
                    tdm.MealType,
                    // 💡 Siguraduhin na Top 2 lang per meal type para sa Dashboard
                    Foods = _db.NtrTemplateMealItems
                        .Where(tmi => tmi.TemplateMealId == tdm.TemplateMealId)
                        .Join(_db.NtrFoodItems,
                              tmi => tmi.FoodId,
                              food => food.FoodId,
                              (tmi, food) => new { tmi, food })
                        .OrderBy(x => x.food.FoodName) // Consistent ordering
                        .Take(2) // 👈 ANTI-BAHA: Top 2 items lang
                        .Select(x => new
                        {
                            x.food.FoodId,
                            x.food.FoodName,
                            x.food.Description,
                            x.food.ImgFilename,
                            x.food.DietaryType,
                            x.food.ServingUnit,
                            x.tmi.DefaultQty,
                            x.food.Calories,
                            x.food.ProteinG,
                            x.food.CarbsG,
                            x.food.FatsG
                        }).ToList()
                }).ToListAsync();

            // 5. Fetch Logs for Status
            var todayLogs = await _db.NtrDailyMealLogs
                .Include(l => l.DailyLog)
                .Where(l => l.DailyLog.UserId == userId && l.DailyLog.PlanDate == todayDateOnly)
                .ToListAsync();

            // 6. Mapping to DTO (Safe for Android)
            var mealGroups = mealGroupsRaw
                .OrderBy(g => g.MealType == "B" ? 1 : g.MealType == "L" ? 2 : g.MealType == "S" ? 3 : 4)
                .Select(group => new MealGroupDto
                {
                    TemplateMealId = group.TemplateMealId,
                    MealType = group.MealType, // I-retain ang "B", "L", etc. para sa logic ng Android
                    Status = todayLogs.Any(l => l.MealType == group.MealType) ? "DONE" : "PENDING",
                    FoodItems = group.Foods.Select(f => new FoodItemDto
                    {
                        FoodId = f.FoodId,
                        Name = f.FoodName,
                        Description = f.Description ?? "",
                        Unit = f.ServingUnit,
                        Qty = (double)f.DefaultQty,
                        Calories = (double)f.Calories,
                        Protein = (double)f.ProteinG,
                        Carbs = (double)f.CarbsG,
                        Fats = (double)f.FatsG,
                        DietaryType = f.DietaryType ?? "balanced",
                        // 💡 Gamit ang baseUrl para sa full path
                        ImageUrl = BuildFoodImageUrl(baseUrl, f.DietaryType ?? "balanced", group.MealType, f.ImgFilename)
                    }).ToList()
                }).ToList();

            // 7. Water intake
            var waterTotal = await _db.NtrWaterLogs
                .Where(w => w.UserId == userId && w.LogDate == todayDateOnly)
                .SumAsync(w => (int?)w.WaterMl) ?? 0;

            // 8. Calculations
            double targetCal = (double)(target?.DailyTargetNetCalories ?? 2000);
            double consumedCal = todayLogs.Sum(l => (double)l.Calories);

            return Ok(new NutritionScreenDto
            {
                TargetCalories = targetCal,
                ConsumedCalories = consumedCal,
                BurnedCalories = burnedCalories,
                RemainingCalories = Math.Max(0, targetCal - (consumedCal - burnedCalories)),

                // Macro Totals (Huwag kalimutan i-map ito, babe!)
                TargetProtein = (double)(target?.ProteinTargetG ?? 150),
                ConsumedProtein = todayLogs.Sum(l => (double)l.ProteinG),
                TargetCarbs = (double)(target?.CarbsTargetG ?? 200),
                ConsumedCarbs = todayLogs.Sum(l => (double)l.CarbsG),
                TargetFats = (double)(target?.FatsTargetG ?? 60),
                ConsumedFats = todayLogs.Sum(l => (double)l.FatsG),

                WaterConsumedMl = waterTotal,
                WaterTargetMl = 2500,
                Meals = mealGroups
            });
        }

        // 💡 Helper revised with BaseURL
        private string BuildFoodImageUrl(string baseUrl, string category, string mealType, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return $"{baseUrl}/images/foods/default.png";

            string catFolder = category.ToLower().Trim().Replace(" ", "_");
            string typeFolder = mealType.ToUpper() switch
            {
                "B" => "breakfast",
                "L" => "lunch",
                "S" => "snacks",
                "D" => "dinner",
                _ => "general"
            };

            return $"{baseUrl}/images/foods/{catFolder}/{typeFolder}/{fileName}";
        }


        [HttpPost("log-full-day")]
        public async Task<IActionResult> LogFullDay([FromBody] LogFullDayRequest req)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var todayDateOnly = DateOnly.FromDateTime(DateTime.UtcNow);

            // 1. Siguraduhin na may Daily Log record na para sa araw na ito
            var dailyLog = await _db.NtrDailyLogs
                .FirstOrDefaultAsync(l => l.UserId == userId && l.PlanDate == todayDateOnly);

            if (dailyLog == null)
            {
                dailyLog = new NtrDailyLog
                {
                    UserId = userId.Value,
                    PlanDate = todayDateOnly,
                    CycleId = req.CycleId
                };
                _db.NtrDailyLogs.Add(dailyLog);
                await _db.SaveChangesAsync();
            }

            // 2. Loop sa bawat meal na pinasa (B, L, S, D)
            foreach (var m in req.Meals)
            {
                var mealLog = await _db.NtrDailyMealLogs
                    .FirstOrDefaultAsync(ml => ml.DailyLogId == dailyLog.DailyLogId && ml.MealType == m.MealType);

                if (mealLog == null)
                {
                    _db.NtrDailyMealLogs.Add(new NtrDailyMealLog
                    {
                        DailyLogId = dailyLog.DailyLogId,
                        MealType = m.MealType,
                        Calories = (int)m.TotalCalories,
                        ProteinG = (decimal)m.TotalProtein,
                        CarbsG = (decimal)m.TotalCarbs,
                        FatsG = (decimal)m.TotalFats
                    });
                }
                else
                {
                    mealLog.Calories = (int)m.TotalCalories;
                    mealLog.ProteinG = (decimal)m.TotalProtein;
                    mealLog.CarbsG = (decimal)m.TotalCarbs;
                    mealLog.FatsG = (decimal)m.TotalFats;
                }
            }

            // 3. 🔥 DITO NA PAPASOK ANG PAG-UPDATE NG CALENDAR STATUS
            var calendarRecord = await _db.NtrMealPlanCalendars
                .FirstOrDefaultAsync(c => c.CycleId == req.CycleId && c.PlanDate == todayDateOnly);

            if (calendarRecord != null)
            {
                calendarRecord.Status = "DONE"; // <--- Mula PENDING, magiging COMPLETED na!
                calendarRecord.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "All meals logged and Calendar updated to COMPLETED!" });
        }

        private int? GetUserId()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }

}