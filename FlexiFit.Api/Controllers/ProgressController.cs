using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Services;
using FlexiFit.Api.Extensions;
using Microsoft.AspNetCore.Authorization;


namespace FlexiFit.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/progress")]
    public class ProgressController : ControllerBase
    {
        private readonly ILogger<ProgressController> _logger;
        private readonly ProgressTrackerService _progressTrackerService;

        public ProgressController(
            ILogger<ProgressController> logger,
            ProgressTrackerService progressTrackerService)
        {
            _logger = logger;
            _progressTrackerService = progressTrackerService;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetProgressStats([FromQuery] string range = "weekly")
        {
            var userId = User.GetUserId();
            if (userId == null) return Unauthorized(new { message = "Invalid user claims." });

            // Delegate the logic from the service (ProgressTrackerService)
            var stats = await _progressTrackerService.GetStatsAsync(userId.Value, range);

            _logger.LogInformation($"Progress stats generated for user {userId}, range: {range}");
            return Ok(stats);
        }
    }
}