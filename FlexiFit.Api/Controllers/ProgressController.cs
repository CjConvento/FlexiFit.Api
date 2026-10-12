using System;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Dtos;
using System.Linq;
using System.Threading.Tasks;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Npgsql;
using System.Collections.Generic;

namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/progress")]
    public class ProgressController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<ProgressController> _logger;
        private readonly ProgressTrackerService _progressTrackerService;

        public ProgressController(
            FlexiFitDbContext context, 
            ILogger<ProgressController> logger,
            ProgressTrackerService progressTrackerService)
        {
            _context = context;
            _logger = logger;
            _progressTrackerService = progressTrackerService;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetProgressStats([FromQuery] string range = "weekly")
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var now = DateTime.UtcNow;
            var startDate = range.ToLower() == "weekly" ? now.AddDays(-7) : now.AddDays(-30);
            int targetDays = range.ToLower() == "weekly" ? 7 : 28;

            try
            {
                // Delegate the logic from the service (ProgressTrackerService)
                var stats = await _progressTrackerService.GetStatsAsync(userId.Value, range); 
                
                _logger.LogInformation($"Progress stats generated for user {userId}, range: {range}");
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting progress stats for user {userId}");
                return StatusCode(500, new { message = "Error fetching progress data", error = ex.Message });
            }
        }

        private int? GetUserId()
        {   
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                    User.FindFirst("user_id")?.Value;
            return int.TryParse(id, out var userId) ? userId : null;
        }
    }
}