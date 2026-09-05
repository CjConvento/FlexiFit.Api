using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;

namespace FlexiFit.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "ADMIN")]
public class AdminController : ControllerBase
{
    private readonly string _connectionString;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IConfiguration configuration, ILogger<AdminController> logger)
    {
        _connectionString = configuration.GetConnectionString("FlexifitDb") ?? "";
        _logger = logger;
    }

    [HttpGet("dashboard-stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        try
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                var totalUsers = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.usr_users");
                var totalWorkouts = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.wrk_workouts");
                var totalFoods = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ntr_food_items");

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
}