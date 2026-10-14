using Microsoft.AspNetCore.Mvc;
using Npgsql; 
using Dapper;
using FlexiFit.Api.DTOs;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace FlexiFit.Api.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize(Roles = "ADMIN")] 
    public class UsersController : ControllerBase
    {
        private readonly UserService _userService;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            ILogger<UsersController> logger,
            UserService userService)
        {
            _userService = userService;
            _logger = logger;
        }

        //==========================
        // ADMIN-ONLY CRUD ENDPOINTS
        //==========================
        [HttpPost("admin-create")]
        public async Task<IActionResult> AdminCreateUser([FromBody] UserCreateDto dto)
        {
            if (dto == null) return BadRequest("User data is required.");

            await _userService.CreateUserAsync(dto);
            
            _logger.LogInformation("Successfully inserted user: {Email}", dto.email);
            return Ok(new { success = true, message = "User created successfully." });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(users);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(int id)
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (user == null) return NotFound(new { message = "User not found." });

            return Ok(user);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateRequest request)
        {
            if (request == null) return BadRequest(new { message = "User data is required." });
            if (id != request.user_id) return BadRequest(new { message = "User ID mismatch." });

            await _userService.UpdateUserAsync(id, request);

            _logger.LogInformation("User {Id} updated successfully.", id);
            return Ok(new { success = true, message = "User updated successfully." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            _logger.LogInformation("API: Attempting to delete user with ID: {Id}", id);

            await _userService.DeleteUserAsync(id);

            return Ok(new { success = true, message = "User deleted successfully." });
        }
    }
}