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

            var today = DateTime.UtcNow.Date;
            var todayDateTime = DateTime.UtcNow;
            var todayDateOnly = DateOnly.FromDateTime(todayDateTime); // Ito ang gagamitin natin

            // 1. Hanapin ang active day sa calendar ni user
            var calendarDay = await _db.NtrMealPlanCalendars
            .Include(c => c.Cycle)
            .FirstOrDefaultAsync(c =>
            c.Cycle.UserId == userId &&
            c.Status == "ACTIVE" &&
            c.PlanDate == todayDateOnly); // Palitan ang 'today' ng 'todayDateOnly'

            if (calendarDay == null) return NotFound("No active meal plan found.");

            // 2. Kunin ang user targets (Math from Profile)
            var target = await _db.NtrUserCycleTargets
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt) // Pinakabagong record sa taas
                .FirstOrDefaultAsync(); // Kunin ang pinaka-una (yung latest)

            // 3. ENGINE: Kunin ang Calories Burned mula sa Activity Summary
            var burnedCalories = await _db.ActActivitySummaries
                .Where(a => a.UserId == userId && a.LogDate == todayDateOnly) // Gamitin ang todayDateOnly
                .SumAsync(a => (double?)a.CaloriesBurned) ?? 0;

            // 4. Kunin ang lahat ng pagkain para sa TemplateDayId
            // --- ITO YUNG NAWAWALA NA VARIABLE ---
            var mealGroupsRaw = await (from tdm in _db.NtrTemplateDayMeals
                                       where tdm.TemplateDayId == calendarDay.TemplateId
                                       select new
                                       {
                                           tdm.TemplateMealId,
                                           tdm.MealType,
                                           Foods = (from tmi in _db.NtrTemplateMealItems
                                                    join food in _db.NtrFoodItems on tmi.FoodId equals food.FoodId
                                                    where tmi.TemplateMealId == tdm.TemplateMealId
                                                    select new
                                                    {
                                                        food.FoodId,
                                                        food.FoodName,
                                                        food.Description,
                                                        food.ImgFilename,
                                                        food.DietaryType, // Siguraduhin na DietaryType na ito
                                                        food.ServingUnit,
                                                        tmi.DefaultQty,
                                                        food.Calories,
                                                        food.ProteinG,
                                                        food.CarbsG,
                                                        food.FatsG
                                                    }).ToList()
                                       }).ToListAsync();

            var mealGroups = mealGroupsRaw.Select(group => new MealGroupDto
            {
                TemplateMealId = group.TemplateMealId,
                MealType = group.MealType,
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

                    // BINAGO: DietaryType na ang gagamitin natin
                    DietaryType = f.DietaryType ?? "balanced",

                    // BINAGO: f.DietaryType na rin ang ipapasa natin sa helper function
                    ImageUrl = BuildFoodImageUrl(f.DietaryType ?? "balanced", group.MealType, f.ImgFilename)
                }).ToList()
            }).ToList();

            // 5. I-sync ang Status (PENDING/DONE/SKIPPED)
            var todayLogs = await _db.NtrDailyMealLogs
                .Include(l => l.DailyLog) // Isama ang parent table
                .Where(l => l.DailyLog.UserId == userId && l.DailyLog.PlanDate == todayDateOnly) // Use todayDateOnly!
                .ToListAsync();

            foreach (var group in mealGroups)
            {
                var log = todayLogs.FirstOrDefault(l => l.MealType == group.MealType);
                group.Status = (log?.DailyLog?.GoalMet == true) ? "DONE" : "PENDING";
            }

            // 6. Water intake
            var waterTotal = await _db.NtrWaterLogs
                .Where(w => w.UserId == userId && w.LogDate == todayDateOnly)
                .SumAsync(w => (int?)w.WaterMl) ?? 0;

            // 7. MATH ENGINE: Compute final stats
            double targetCal = (double)(target?.DailyTargetNetCalories ?? 0);
            double consumedCal = todayLogs.Where(l => l.DailyLog.GoalMet == true).Sum(l => (double)l.Calories);

            // Formula: Net = Consumed - Burned
            double netCalories = consumedCal - burnedCalories;

            var response = new NutritionScreenDto
            {
                // Targets
                TargetCalories = targetCal,
                TargetProtein = (double)(target?.ProteinTargetG ?? 0),
                TargetCarbs = (double)(target?.CarbsTargetG ?? 0),
                TargetFats = (double)(target?.FatsTargetG ?? 0),

                // Consumed
                ConsumedCalories = consumedCal,
                ConsumedProtein = todayLogs.Where(l => l.DailyLog.GoalMet == true).Sum(l => (double)l.ProteinG),
                ConsumedCarbs = todayLogs.Where(l => l.DailyLog.GoalMet == true).Sum(l => (double)l.CarbsG),
                ConsumedFats = todayLogs.Where(l => l.DailyLog.GoalMet == true).Sum(l => (double)l.FatsG),

                // Engine Results
                BurnedCalories = burnedCalories,
                NetCalories = netCalories,
                RemainingCalories = targetCal - netCalories,

                // Others
                WaterConsumedMl = waterTotal,
                WaterTargetMl = 2500, // Pwedeng gawing dynamic ito soon
                Meals = mealGroups
            };

            return Ok(response);
        }

        // --- DINAGDAG: New Helper Function ---
        // Ito ang logic na nag-ma-map ng Database values papunta sa physical wwwroot folders mo.
        private string BuildFoodImageUrl(string category, string mealType, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "images/foods/default.png";

            // Ginagawang lowercase at pinapalitan ang space ng underscore (e.g. "High Protein" -> "high_protein")
            string catFolder = category.ToLower().Trim().Replace(" ", "_");

            // Dino-double check kung "B", "L", "S", "D" lang ang galing DB, i-map sa full folder names
            string typeFolder = mealType.ToUpper() switch
            {
                "B" => "breakfast",
                "L" => "lunch",
                "S" => "snacks",
                "D" => "dinner",
                _ => mealType.ToLower().Trim() // Kung "breakfast" na ang nakalagay sa DB, ok na 'to
            };

            return $"images/foods/{catFolder}/{typeFolder}/{fileName}";
        }

        private int? GetUserId()
        {
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("user_id")?.Value;
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }
}