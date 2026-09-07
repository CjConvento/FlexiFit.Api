using Microsoft.AspNetCore.Mvc;
using Npgsql; 
using Dapper;
using FlexiFit.Api.DTOs;
using Microsoft.Extensions.Hosting;  // for IHostEnvironment
using Microsoft.AspNetCore.Authorization;

namespace FlexiFit.Api.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = "ADMIN")] 
    public class UsersController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<UsersController> _logger;
        private readonly IHostEnvironment _env; // to check environment

        public UsersController(IConfiguration configuration, ILogger<UsersController> logger, IHostEnvironment env)
        {
            _connectionString = configuration.GetConnectionString("FlexifitDb") ?? "";
            _logger = logger;
            _env = env;

            // Print to console for sure
            Console.WriteLine($"UsersController: connection string = {_connectionString}");

            // Optional: test the connection immediately
            try
            {
                using var testConnection = new NpgsqlConnection(_connectionString);
                testConnection.Open();
                Console.WriteLine("UsersController: database connection successful!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UsersController: connection test failed: {ex.Message}");
                _logger.LogError(ex, "Connection test failed in constructor");
            }
        }

        [HttpPost("admin-create")]
        public async Task<IActionResult> AdminCreateUser([FromBody] UserCreateDto dto)
        {
            if (dto == null) return BadRequest("User data is required.");

            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    var sql = @"INSERT INTO usr_users 
                                (firebase_uid, email, name, username, is_verified, role, status, auth_provider, created_at, updated_at) 
                                VALUES 
                                (@firebase_uid, @email, @name, @username, 1, @role, 'ACTIVE', @auth_provider, CURRENT_TIMESTAMP)";

                    await connection.ExecuteAsync(sql, dto);
                    _logger.LogInformation("Successfully inserted user: {Email}", dto.email);

                    return Ok(new { success = true, message = "User created successfully." });
                }
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError("PostgreSQL Error: {Message}", ex.Message);
                if (ex.SqlState == "23505")
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
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    var sql = "SELECT * FROM usr_users ORDER BY created_at DESC";
                    var users = await connection.QueryAsync(sql);
                    return Ok(users);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users");

                // Return detailed error in development
                if (_env.IsDevelopment())
                {
                    return StatusCode(500, $"Error: {ex.Message}\nInner: {ex.InnerException?.Message}");
                }
                return StatusCode(500, "Could not fetch users from database.");
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    var sql = @"SELECT user_id, firebase_uid, name, username, email, 
                               role, status, is_verified, auth_provider, 
                               created_at, updated_at 
                        FROM usr_users 
                        WHERE user_id = @id";
                    var user = await connection.QueryFirstOrDefaultAsync(sql, new { id });
                    if (user == null)
                        return NotFound(new { message = "User not found." });

                    return Ok(user);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error fetching user {Id}: {Message}", id, ex.Message);
                return StatusCode(500, "Error fetching user.");
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateRequest request)
        {
            if (request == null)
                return BadRequest("User data is required.");

            if (id != request.user_id)
                return BadRequest("User ID mismatch.");

            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    // Check if user exists
                    var exists = await connection.ExecuteScalarAsync<bool>(
                        "SELECT COUNT(1) FROM usr_users WHERE user_id = @id", new { id });
                    if (!exists)
                        return NotFound(new { message = "User not found." });

                    var sql = @"UPDATE usr_users 
                        SET name = @name,
                            username = @username,
                            email = @email,
                            role = @role,
                            updated_at = CURRENT_TIMESTAMP
                        WHERE user_id = @user_id";

                    int rows = await connection.ExecuteAsync(sql, request);
                    if (rows > 0)
                    {
                        _logger.LogInformation("User {Id} updated successfully.", id);
                        return Ok(new { success = true, message = "User updated successfully." });
                    }

                    return BadRequest("Update failed.");
                }
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError("PostgreSQL Error updating user {Id}: {Message}", id, ex.Message);
                if (ex.SqlState == "23505")
                    return Conflict("Duplicate Entry: Email or Username already exists.");
                return StatusCode(500, "Database error.");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error updating user {Id}: {Message}", id, ex.Message);
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            _logger.LogInformation("API: Attempting to delete user with ID: {Id}", id);

            try
            {
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    // 1. I-check muna kung existing ang user
                    var checkSql = "SELECT COUNT(1) FROM usr_users WHERE user_id = @id";
                    var exists = await connection.ExecuteScalarAsync<bool>(checkSql, new { id });

                    if (!exists)
                    {
                        _logger.LogWarning("API: User with ID {Id} not found.", id);
                        return NotFound(new { message = "User not found." });
                    }

                    // 2. Execute Delete
                    var deleteSql = "DELETE FROM usr_users WHERE user_id = @id";
                    int affectedRows = await connection.ExecuteAsync(deleteSql, new { id });

                    if (affectedRows > 0)
                    {
                        _logger.LogInformation("API: Successfully deleted user ID: {Id}", id);
                        return Ok(new { success = true, message = "User deleted successfully." });
                    }

                    return BadRequest("Failed to delete user.");
                }
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError("PostgreSQL Error during delete: {Message}", ex.Message);

                // Error Number 547 ay Foreign Key violation (halimbawa: may workout logs na ang user)
                if (ex.SqlState == "23503")
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

        public class UserUpdateRequest
        {
            public int user_id { get; set; }
            public string? name { get; set; }
            public string username { get; set; } = string.Empty;
            public string email { get; set; } = string.Empty;
            public string role { get; set; } = string.Empty;
        }
    }
}