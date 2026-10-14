using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Services;
using FlexiFit.Api.Entities;
using Npgsql;
using System.Data;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/actlogs")]
    [Authorize(Roles = "ADMIN")]
    public class ActivityLogsController : ControllerBase
    {
        private readonly ILogger<ActivityLogsController> _logger;
        private readonly ActivityLogService _ActivityLogService;

        public ActivityLogsController(
            ILogger<ActivityLogsController> logger,
            ActivityLogService activityLogService)
        {
            _logger = logger;
            _ActivityLogService = activityLogService;
        }

        /// <summary>
        /// GET: api/actlogs/admin/all
        /// </summary>
        [HttpGet("admin/all")]
        public async Task<ActionResult<object>> AdminGetAllLogs(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            _logger.LogInformation("📡 ADMIN: Fetching all activity logs");

            // Tawagin lang ang service. Ang complex raw SQL at pagination ay nasa loob na.
            var result = await _ActivityLogService.GetAllLogsAsync(search, fromDate, toDate, page, pageSize);

            return Ok(result);
        }

        /// <summary>
        /// GET: api/actlogs/admin/{id}
        /// </summary>
        [HttpGet("admin/{id}")]
        public async Task<ActionResult<object>> AdminGetLog(int id)
        {
            _logger.LogInformation($"📡 ADMIN: Fetching activity log for user ID: {id}");

            // Kung wala ang user, magtethrow ang Service ng KeyNotFoundException (404 via Middleware)
            var result = await _ActivityLogService.GetLogByUserIdAsync(id);

            return Ok(result);
        }

        /// <summary>
        /// DELETE: api/actlogs/admin/{id}
        /// </summary>
        [HttpDelete("admin/{id}")]
        public async Task<IActionResult> AdminDeleteLog(int id)
        {
            _logger.LogInformation($"📡 ADMIN: Deleting activity logs for user ID: {id}");

            // ExecuteDeleteAsync ang gagamitin sa loob.
            await _ActivityLogService.DeleteLogsByUserIdAsync(id);
            return Ok(new { message = "Activity logs deleted successfully." });
        }
    }
}