using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using FlexiFit.Api.DTOs;

namespace FlexiFit.Api.Controllers
{
    // Ginawa nating lowercase ang route ("users" imbes na "[controller]")
    [Route("api/users")]
    [ApiController]
    public class UsersController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<UsersController> _logger; // Dagdag para sa logs sa API side

        public UsersController(IConfiguration configuration, ILogger<UsersController> logger)
        {
            // Siguraduhing "FlexifitDb" ang nasa appsettings.json mo
            _connectionString = "Server=192.168.1.246,1433;Database=FLEXIFIT;User Id=cy;Password=;TrustServerCertificate=True;"; 
            _logger = logger;
        }

        [HttpPost("admin-create")]
        public async Task<IActionResult> AdminCreateUser([FromBody] UserCreateDto dto)
        {
            if (dto == null) return BadRequest("User data is required.");

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    var sql = @"INSERT INTO dbo.Usr_Users 
                                (firebase_uid, email, name, username, is_verified, role, status, auth_provider, created_at, updated_at) 
                                VALUES 
                                (@firebase_uid, @email, @name, @username, 1, @role, 'active', @auth_provider, GETDATE(), GETDATE())";

                    await connection.ExecuteAsync(sql, dto);
                    _logger.LogInformation("Successfully inserted user: {Email}", dto.email);

                    return Ok(new { success = true, message = "User created successfully." });
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError("SQL Error: {Message}", ex.Message);
                if (ex.Number == 2627 || ex.Number == 2601)
                    return Conflict("Duplicate Entry: Email or Username already exists.");

                return StatusCode(500, $"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Critical API Error: {Message}", ex.Message);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    // Mahalaga: Siguraduhing ang columns dito ay tugma sa Admin Panel User Model
                    var sql = "SELECT * FROM dbo.Usr_Users ORDER BY created_at DESC";
                    var users = await connection.QueryAsync(sql);
                    return Ok(users);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching users: {Message}", ex.Message);
                return StatusCode(500, "Could not fetch users from database.");
            }
        }


        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            _logger.LogInformation("API: Attempting to delete user with ID: {Id}", id);

            try
            {
                using (var connection = new SqlConnection(_connectionString))
                {
                    // 1. I-check muna kung existing ang user
                    var checkSql = "SELECT COUNT(1) FROM dbo.Usr_Users WHERE user_id = @id";
                    var exists = await connection.ExecuteScalarAsync<bool>(checkSql, new { id });

                    if (!exists)
                    {
                        _logger.LogWarning("API: User with ID {Id} not found.", id);
                        return NotFound(new { message = "User not found." });
                    }

                    // 2. Execute Delete
                    var deleteSql = "DELETE FROM dbo.Usr_Users WHERE user_id = @id";
                    int affectedRows = await connection.ExecuteAsync(deleteSql, new { id });

                    if (affectedRows > 0)
                    {
                        _logger.LogInformation("API: Successfully deleted user ID: {Id}", id);
                        return Ok(new { success = true, message = "User deleted successfully." });
                    }

                    return BadRequest("Failed to delete user.");
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError("SQL Error during delete: {Message}", ex.Message);

                // Error Number 547 ay Foreign Key violation (halimbawa: may workout logs na ang user)
                if (ex.Number == 547)
                {
                    return BadRequest("Hindi mabura ang user dahil mayroon itong kaugnay na data sa ibang table.");
                }

                return StatusCode(500, $"Database error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError("Critical API Error during delete: {Message}", ex.Message);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}