using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Dapper;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN")]
public class AdminController : ControllerBase
{
    private readonly string _connectionString;
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IConfiguration configuration, 
        FlexiFitDbContext db,
        ILogger<AdminController> logger)
    {
        _connectionString = configuration.GetConnectionString("FlexifitDb") ?? "";
        _db = db;
        _logger = logger;
    }

    [HttpGet("dashboard-stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        try
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                var totalUsers = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usr_users");
                var totalWorkouts = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wrk_workouts");
                var totalFoods = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ntr_food_items");

                return Ok(new
                {
                    TotalUsers = totalUsers,
                    TotalWorkouts = totalWorkouts,
                    TotalFoods = totalFoods
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dashboard stats");
            return StatusCode(500, new { error = "Failed to fetch dashboard stats" });
        }
    }

    // ==========================================
    // ADMIN CRUD ENDPOINTS FOR FOODS
    // ==========================================

    /// <summary>
    /// GET: api/admin/foods
    /// </summary>
    [HttpGet("admin/foods")]
    public async Task<ActionResult<object>> AdminGetAllFoods(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null)
    {
        try
        {
            //  Validate inputs
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100; // Max limit para iwas abuse

            _logger.LogInformation("📡 ADMIN: Fetching foods — Page {Page}, Size {PageSize}, Search: {Search}", page, pageSize, search);
            
            //  Base query
            var query = _db.NtrFoodItems.AsNoTracking().AsQueryable();

            //  Optional search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(f =>
                    f.FoodName.ToLower().Contains(lowerSearch) ||
                    f.MealType!.ToLower().Contains(lowerSearch) ||
                    f.DietaryType!.ToLower().Contains(lowerSearch) ||
                    f.Category!.ToLower().Contains(lowerSearch));
            }

            //  Get total count BEFORE pagination
            var totalCount = await query.CountAsync();

            //  Apply pagination + sorting
            var foods = await query
                .OrderBy(f => f.FoodName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            _logger.LogInformation(" ADMIN: Retrieved {Count} foods (Page {Page}/{TotalPages}, Total: {Total})",
            foods.Count, page, totalPages, totalCount);

            //  Return paginated response
            return Ok(new
            {
                data = foods,
                total = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = totalPages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching foods");
            return StatusCode(500, new { error = "An error occurred while fetching foods." });
        }
    }

    /// <summary>
    /// GET: api/nutrition/admin/foods/{id}
    /// </summary>
    [HttpGet("admin/foods/{id}")]
    public async Task<ActionResult<NtrFoodItem>> AdminGetFood(int id)
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Fetching food ID: {Id}", id);
            var food = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == id);

            if (food == null)
            {
                _logger.LogWarning("❌ ADMIN: Food not found: {Id}", id);
                return NotFound(new { error = $"Food with ID {id} not found." });
            }

            return Ok(food);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching food ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the food." });
        }
    }

    /// <summary>
    /// POST: api/nutrition/admin/foods
    /// </summary>
    [HttpPost("admin/foods")]
    public async Task<ActionResult<NtrFoodItem>> AdminCreateFood([FromBody] NtrFoodItem food)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ ADMIN: Invalid model state for food creation");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("📡 ADMIN: Creating new food: {Name}", food.FoodName);

            food.CreatedAt = DateTime.UtcNow;
            food.UpdatedAt = DateTime.UtcNow;
            food.IsActive = true;

            _db.NtrFoodItems.Add(food);
            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Food created with ID: {Id}", food.FoodId);
            return CreatedAtAction(nameof(AdminGetFood), new { id = food.FoodId }, food);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error creating food: {Name}", food.FoodName);
            return StatusCode(500, new { error = "An error occurred while creating the food." });
        }
    }

    /// <summary>
    /// PUT: api/nutrition/admin/foods/{id}
    /// </summary>
    [HttpPut("admin/foods/{id}")]
    public async Task<IActionResult> AdminUpdateFood(int id, [FromBody] NtrFoodItem food)
    {
        try
        {
            if (id != food.FoodId)
            {
                _logger.LogWarning("❌ ADMIN: ID mismatch: {Id} vs {FoodId}", id, food.FoodId);
                return BadRequest(new { error = "ID mismatch." });
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ ADMIN: Invalid model state for food update");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("📡 ADMIN: Updating food ID: {Id}", id);

            var existing = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == id);

            if (existing == null)
            {
                _logger.LogWarning("❌ ADMIN: Food not found: {Id}", id);
                return NotFound(new { error = $"Food with ID {id} not found." });
            }

            // Update fields
            existing.FoodName = food.FoodName;
            existing.MealType = food.MealType;
            existing.DietaryType = food.DietaryType;
            existing.Category = food.Category;
            existing.SizeType = food.SizeType;
            existing.ServingUnit = food.ServingUnit;
            existing.ServingWeightG = food.ServingWeightG;
            existing.Calories = food.Calories;
            existing.ProteinG = food.ProteinG;
            existing.CarbsG = food.CarbsG;
            existing.FatsG = food.FatsG;
            existing.IsActive = food.IsActive;
            existing.ImgFilename = food.ImgFilename;
            existing.Description = food.Description;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Food updated: {Id}", id);
            return Ok(new { message = "Food updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error updating food ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while updating the food." });
        }
    }

    /// <summary>
    /// DELETE: api/nutrition/admin/foods/{id}
    /// </summary>
    [HttpDelete("admin/foods/{id}")]
    public async Task<IActionResult> AdminDeleteFood(int id)
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Deleting food ID: {Id}", id);

            var food = await _db.NtrFoodItems
                .FirstOrDefaultAsync(f => f.FoodId == id);

            if (food == null)
            {
                _logger.LogWarning("❌ ADMIN: Food not found: {Id}", id);
                return NotFound(new { error = $"Food with ID {id} not found." });
            }

            _db.NtrFoodItems.Remove(food);
            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Food deleted: {Id}", id);
            return Ok(new { message = "Food deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error deleting food ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the food." });
        }
    }

    // ==========================================
    // ADMIN CRUD ENDPOINTS FOR WORKOUTS
    // ==========================================

    /// <summary>
    /// GET: api/workout/admin/all
    /// </summary>
    [HttpGet("admin/all")]
    public async Task<ActionResult<object>> AdminGetAllWorkouts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null)
    {
        try
        {
            //  Validate inputs
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100; // Max limit para iwas abuse

            _logger.LogInformation("📡 ADMIN: Fetching workouts — Page {Page}, Size {PageSize}",page, pageSize);

            //  Base query
            var query = _db.WrkWorkouts.AsNoTracking().AsQueryable();

            //  Optional search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(w =>
                    w.WorkoutName.ToLower().Contains(lowerSearch) ||
                    w.MuscleGroup!.ToLower().Contains(lowerSearch) ||
                    w.Equipment!.ToLower().Contains(lowerSearch));
            }

            //  Get total count BEFORE pagination
            var totalCount = await query.CountAsync();

            //  Apply pagination
            var workouts = await query
                .OrderBy(w => w.WorkoutId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

             _logger.LogInformation(" ADMIN: Retrieved {Count} workouts (Page {Page}/{TotalPages}, Total: {Total})",
            workouts.Count, page, totalPages, totalCount);

            //  Return paginated response
            return Ok(new
            {
                data = workouts,
                total = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = totalPages
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching workouts");
            return StatusCode(500, new { error = "An error occurred while fetching workouts." });
        }
    }
                                                            
    /// <summary>
    /// GET: api/workout/admin/{id}
    /// </summary>
    [HttpGet("admin/{id}")]
    public async Task<ActionResult<WrkWorkout>> AdminGetWorkout(int id)
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Fetching workout ID: {Id}", id);

            var workout = await _db.WrkWorkouts
                .FirstOrDefaultAsync(w => w.WorkoutId == id);

            if (workout == null)
            {
                _logger.LogWarning("❌ ADMIN: Workout not found: {Id}", id);
                return NotFound(new { error = $"Workout with ID {id} not found." });
            }

            return Ok(workout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching workout ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while fetching the workout." });
        }
    }

    /// <summary>
    /// POST: api/workout/admin/create
    /// </summary>
    [HttpPost("admin/create")]
    public async Task<ActionResult<WrkWorkout>> AdminCreateWorkout([FromBody] WrkWorkout workout)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ ADMIN: Invalid model state for workout creation");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("📡 ADMIN: Creating new workout: {Name}", workout.WorkoutName);

            workout.CreatedAt = DateTime.UtcNow;
            workout.UpdatedAt = DateTime.UtcNow;
            workout.IsActive = false; // Default: inactive (needs admin approval)

            _db.WrkWorkouts.Add(workout);
            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Workout created with ID: {Id}", workout.WorkoutId);
            return CreatedAtAction(nameof(AdminGetWorkout), new { id = workout.WorkoutId }, workout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error creating workout: {Name}", workout.WorkoutName);
            return StatusCode(500, new { error = "An error occurred while creating the workout." });
        }
    }

    /// <summary>
    /// PUT: api/workout/admin/{id}
    /// </summary>
    [HttpPut("admin/{id}")]
    public async Task<IActionResult> AdminUpdateWorkout(int id, [FromBody] WrkWorkout workout)
    {
        try
        {
            if (id != workout.WorkoutId)
            {
                _logger.LogWarning("❌ ADMIN: ID mismatch: {Id} vs {WorkoutId}", id, workout.WorkoutId);
                return BadRequest(new { error = "ID mismatch." });
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("❌ ADMIN: Invalid model state for workout update");
                return BadRequest(ModelState);
            }

            _logger.LogInformation("📡 ADMIN: Updating workout ID: {Id}", id);

            var existing = await _db.WrkWorkouts
                .FirstOrDefaultAsync(w => w.WorkoutId == id);

            if (existing == null)
            {
                _logger.LogWarning("❌ ADMIN: Workout not found: {Id}", id);
                return NotFound(new { error = $"Workout with ID {id} not found." });
            }

            // Update fields
            existing.WorkoutName = workout.WorkoutName;
            existing.MuscleGroup = workout.MuscleGroup;
            existing.Equipment = workout.Equipment;
            existing.Environment = workout.Environment;
            existing.Category = workout.Category;
            existing.DifficultyLevel = workout.DifficultyLevel;
            existing.IsWeighted = workout.IsWeighted;
            existing.Notes = workout.Notes;
            existing.CaloriesBurned = workout.CaloriesBurned;
            existing.IsActive = workout.IsActive;
            existing.ImgFilename = workout.ImgFilename;
            existing.Duration = workout.Duration;
            existing.VideoUrl = workout.VideoUrl;
            existing.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Workout updated: {Id}", id);
            return Ok(new { message = "Workout updated successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error updating workout ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while updating the workout." });
        }
    }

    /// <summary>
    /// DELETE: api/workout/admin/{id}
    /// </summary>
    [HttpDelete("admin/{id}")]
    public async Task<IActionResult> AdminDeleteWorkout(int id)
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Deleting workout ID: {Id}", id);

            var workout = await _db.WrkWorkouts
                .FirstOrDefaultAsync(w => w.WorkoutId == id);

            if (workout == null)
            {
                _logger.LogWarning("❌ ADMIN: Workout not found: {Id}", id);
                return NotFound(new { error = $"Workout with ID {id} not found." });
            }

            _db.WrkWorkouts.Remove(workout);
            await _db.SaveChangesAsync();

            _logger.LogInformation(" ADMIN: Workout deleted: {Id}", id);
            return Ok(new { message = "Workout deleted successfully." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error deleting workout ID: {Id}", id);
            return StatusCode(500, new { error = "An error occurred while deleting the workout." });
        }
    }

    /// <summary>
    /// GET: api/workout/admin/tutorials
    /// </summary>
    [HttpGet("admin/tutorials")]
    public async Task<ActionResult<IEnumerable<object>>> AdminGetTutorials()
    {
        try
        {
            _logger.LogInformation("📡 ADMIN: Fetching workouts with video tutorials");

            var tutorials = await _db.WrkWorkouts
                .Where(w => !string.IsNullOrEmpty(w.VideoUrl))
                .OrderBy(w => w.WorkoutName)
                .Select(w => new
                {
                    w.WorkoutId,
                    w.WorkoutName,
                    w.VideoUrl
                })
                .ToListAsync();

            _logger.LogInformation(" ADMIN: Retrieved {Count} tutorials", tutorials.Count);
            return Ok(tutorials);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ADMIN: Error fetching tutorials");
            return StatusCode(500, new { error = "An error occurred while fetching tutorials." });
        }
    }
}
