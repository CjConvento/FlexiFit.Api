using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Dapper;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN")]
public class AdminController : ControllerBase
{
    private readonly AdminService _adminService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AdminService adminService,
        ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    [HttpGet("dashboard-stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var stats = await _adminService.GetDashboardStatsAsync();
        return Ok(stats);
    }

    // ==========================================
    // ADMIN CRUD ENDPOINTS FOR FOODS
    // ==========================================

    /// <summary>
    /// GET: api/admin/foods
    /// </summary>
    [HttpGet("/api/nutrition/admin/foods")]
    public async Task<ActionResult> AdminGetAllFoods(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = "all")
    {
       var result = await _adminService.GetFoodsAsync(page, pageSize, search, status);
       return Ok(result);
    }

    /// <summary>
    /// GET: api/nutrition/admin/foods/{id}
    /// </summary>
    [HttpGet("/api/nutrition/admin/foods/{id}")]
    public async Task<ActionResult> AdminGetFood(int id)
    {
        var food = await _adminService.GetFoodByIdAsync(id);
        return Ok(food);
    }

    /// <summary>
    /// POST: api/nutrition/admin/foods
    /// </summary>
    [HttpPost("/api/nutrition/admin/foods")]
    public async Task<ActionResult> AdminCreateFood([FromBody] NtrFoodItem food)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var createdFood = await _adminService.CreateFoodAsync(food);
        return CreatedAtAction(nameof(AdminGetFood), new { id = createdFood.FoodId }, createdFood);
    }

    /// <summary>
    /// PUT: api/nutrition/admin/foods/{id}
    /// </summary>
    [HttpPut("/api/nutrition/admin/foods/{id}")]
    public async Task<IActionResult> AdminUpdateFood(int id, [FromBody] NtrFoodItem food)
    {
        if (id != food.FoodId) return BadRequest(new { error = "ID mismatch." });
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _adminService.UpdateFoodAsync(id, food);
        return Ok(new { message = "Food updated successfully." });
    }

    /// <summary>
    /// DELETE: api/nutrition/admin/foods/{id}
    /// </summary>
    [HttpDelete("/api/nutrition/admin/foods/{id}")]
    public async Task<IActionResult> AdminDeleteFood(int id)
    {
        await _adminService.DeleteFoodAsync(id);
        return Ok(new { message = "Food deleted successfully." });
    }

    // ==========================================
    // ADMIN CRUD ENDPOINTS FOR WORKOUTS
    // ==========================================

    /// <summary>
    /// GET: api/workout/admin/all
    /// </summary>
    [HttpGet("/api/workout/admin/all")]
    public async Task<ActionResult> AdminGetAllWorkouts(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = "all")
    {
        var result = await _adminService.GetWorkoutsAsync(page, pageSize, search, status);
        return Ok(result);
    }
                                                            
    /// <summary>
    /// GET: api/workout/admin/{id}
    /// </summary>
    [HttpGet("/api/workout/admin/{id}")]
    public async Task<ActionResult> AdminGetWorkout(int id)
    {
        var workout = await _adminService.GetWorkoutByIdAsync(id);
        return Ok(workout);
    }

    /// <summary>
    /// POST: api/workout/admin/create
    /// </summary>
    [HttpPost("/api/workout/admin/create")]
    public async Task<ActionResult> AdminCreateWorkout([FromBody] WrkWorkout workout)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var createdWorkout = await _adminService.CreateWorkoutAsync(workout);
        return CreatedAtAction(nameof(AdminGetWorkout), new { id = createdWorkout.WorkoutId }, createdWorkout);
    }

    /// <summary>
    /// PUT: api/workout/admin/{id}
    /// </summary>
    [HttpPut("/api/workout/admin/{id}")]
    public async Task<IActionResult> AdminUpdateWorkout(int id, [FromBody] WrkWorkout workout)
    {
        if (id != workout.WorkoutId) return BadRequest(new { error = "ID mismatch." });
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _adminService.UpdateWorkoutAsync(id, workout);
        return Ok(new { message = "Workout updated successfully." });
    }

    /// <summary>
    /// DELETE: api/workout/admin/{id}
    /// </summary>
    [HttpDelete("/api/workout/admin/{id}")]
    public async Task<IActionResult> AdminDeleteWorkout(int id)
    {
        await _adminService.DeleteWorkoutAsync(id);
        return Ok(new { message = "Workout deleted successfully." });
    }

    /// <summary>
    /// GET: api/workout/admin/tutorials
    /// </summary>
    [HttpGet("/api/workout/admin/tutorials")]
    public async Task<ActionResult<IEnumerable<object>>> AdminGetTutorials()
    {
        var tutorials = await _adminService.GetTutorialsAsync();
        return Ok(tutorials);
    }
}
