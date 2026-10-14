using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Dapper;

namespace FlexiFit.Api.Services;

public class AdminService
{
    private readonly FlexiFitDbContext _db;
    private readonly string _connectionString;

    public AdminService(FlexiFitDbContext db, IConfiguration configuration)
    {
        _db = db;
        _connectionString = configuration.GetConnectionString("FlexifitDb") 
            ?? throw new InvalidOperationException("Connection string 'FlexifitDb' not found.");
    }

    // ==========================================
    // DASHBOARD STATS
    // ==========================================
    public async Task<object> GetDashboardStatsAsync()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        var totalUsers = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM usr_users");
        var totalWorkouts = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM wrk_workouts");
        var totalFoods = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ntr_food_items");

        return new { TotalUsers = totalUsers, TotalWorkouts = totalWorkouts, TotalFoods = totalFoods };
    }

    // ==========================================
    // FOODS CRUD
    // ==========================================
    public async Task<object> GetFoodsAsync(int page, int pageSize, string? search)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var query = _db.NtrFoodItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowerSearch = search.ToLower();
            query = query.Where(f => f.FoodName.ToLower().Contains(lowerSearch) || 
                                     f.MealType!.ToLower().Contains(lowerSearch) || 
                                     f.DietaryType!.ToLower().Contains(lowerSearch) || 
                                     f.Category!.ToLower().Contains(lowerSearch));
        }

        var totalCount = await query.CountAsync();
        var foods = await query.OrderBy(f => f.FoodName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new { data = foods, total = totalCount, page, pageSize, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) };
    }

    public async Task<NtrFoodItem> GetFoodByIdAsync(int id)
    {
        var food = await _db.NtrFoodItems.FirstOrDefaultAsync(f => f.FoodId == id);
        if (food == null) throw new KeyNotFoundException($"Food with ID {id} not found.");
        return food;
    }

    public async Task<NtrFoodItem> CreateFoodAsync(NtrFoodItem food)
    {
        food.CreatedAt = DateTime.UtcNow;
        food.UpdatedAt = DateTime.UtcNow;
        food.IsActive = true;
        _db.NtrFoodItems.Add(food);
        await _db.SaveChangesAsync();
        return food;
    }

    public async Task UpdateFoodAsync(int id, NtrFoodItem food)
    {
        var existing = await _db.NtrFoodItems.FirstOrDefaultAsync(f => f.FoodId == id);
        if (existing == null) throw new KeyNotFoundException($"Food with ID {id} not found.");

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
    }

    public async Task DeleteFoodAsync(int id)
    {
        var food = await _db.NtrFoodItems.FirstOrDefaultAsync(f => f.FoodId == id);
        if (food == null) throw new KeyNotFoundException($"Food with ID {id} not found.");
        
        _db.NtrFoodItems.Remove(food);
        await _db.SaveChangesAsync();
    }

    // ==========================================
    // WORKOUTS CRUD (DRY: Reusing similar patterns)
    // ==========================================
    public async Task<object> GetWorkoutsAsync(int page, int pageSize, string? search)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100;

        var query = _db.WrkWorkouts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var lowerSearch = search.ToLower();
            query = query.Where(w => w.WorkoutName.ToLower().Contains(lowerSearch) || 
                                     w.MuscleGroup!.ToLower().Contains(lowerSearch) || 
                                     w.Equipment!.ToLower().Contains(lowerSearch));
        }

        var totalCount = await query.CountAsync();
        var workouts = await query.OrderBy(w => w.WorkoutId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new { data = workouts, total = totalCount, page, pageSize, totalPages = (int)Math.Ceiling(totalCount / (double)pageSize) };
    }

    public async Task<WrkWorkout> GetWorkoutByIdAsync(int id)
    {
        var workout = await _db.WrkWorkouts.FirstOrDefaultAsync(w => w.WorkoutId == id);
        if (workout == null) throw new KeyNotFoundException($"Workout with ID {id} not found.");
        return workout;
    }

    public async Task<WrkWorkout> CreateWorkoutAsync(WrkWorkout workout)
    {
        workout.CreatedAt = DateTime.UtcNow;
        workout.UpdatedAt = DateTime.UtcNow;
        workout.IsActive = false; // Default: needs admin approval
        _db.WrkWorkouts.Add(workout);
        await _db.SaveChangesAsync();
        return workout;
    }

    public async Task UpdateWorkoutAsync(int id, WrkWorkout workout)
    {
        var existing = await _db.WrkWorkouts.FirstOrDefaultAsync(w => w.WorkoutId == id);
        if (existing == null) throw new KeyNotFoundException($"Workout with ID {id} not found.");

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
    }

    public async Task DeleteWorkoutAsync(int id)
    {
        var workout = await _db.WrkWorkouts.FirstOrDefaultAsync(w => w.WorkoutId == id);
        if (workout == null) throw new KeyNotFoundException($"Workout with ID {id} not found.");
        
        _db.WrkWorkouts.Remove(workout);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<object>> GetTutorialsAsync()
    {
        return await _db.WrkWorkouts
            .Where(w => !string.IsNullOrEmpty(w.VideoUrl))
            .OrderBy(w => w.WorkoutName)
            .Select(w => new { w.WorkoutId, w.WorkoutName, w.VideoUrl })
            .ToListAsync();
    }
}