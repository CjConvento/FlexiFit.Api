using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Entities;

namespace FlexiFit.Api.Controllers
{
    [ApiController]
    [Route("api/actlogs")]
    [Authorize(Roles = "ADMIN")]
    public class ActivityLogsController : ControllerBase
    {
        private readonly FlexiFitDbContext _context;
        private readonly ILogger<ActivityLogsController> _logger;

        public ActivityLogsController(FlexiFitDbContext context, ILogger<ActivityLogsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region Admin CRUD Endpoints

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
            try
            {
                _logger.LogInformation("📡 ADMIN: Fetching all activity logs");

                // ✅ Gumamit ng ActActivitySummary at i-join sa UsrUser
                var query = from a in _context.ActActivitySummaries
                            join u in _context.UsrUsers on a.UserId equals u.UserId
                            select new
                            {
                                a.SummaryId,
                                a.UserId,
                                a.CaloriesBurned,
                                a.TotalMinutes,
                                a.LogDate,
                                a.UpdatedAt,
                                Username = u.Username,
                                Email = u.Email,
                                // Combine into a single detail string
                                Details = $"{a.CaloriesBurned} calories burned in {a.TotalMinutes} minutes"
                            };

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(a =>
                        a.Username.Contains(search) ||
                        a.Email.Contains(search) ||
                        a.Details.Contains(search));
                }

                if (fromDate.HasValue)
                {
                    var fromDateOnly = DateOnly.FromDateTime(fromDate.Value);
                    query = query.Where(a => a.LogDate >= fromDateOnly);
                }

                if (toDate.HasValue)
                {
                    var toDateOnly = DateOnly.FromDateTime(toDate.Value);
                    query = query.Where(a => a.LogDate <= toDateOnly);
                }

                var total = await query.CountAsync();

                var logs = await query
                    .OrderByDescending(a => a.LogDate)
                    .ThenByDescending(a => a.UpdatedAt)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new
                    {
                        a.SummaryId,
                        a.UserId,
                        a.Username,
                        a.Email,
                        a.CaloriesBurned,
                        a.TotalMinutes,
                        a.LogDate,
                        a.UpdatedAt,
                        a.Details,
                        ActivityType = "Workout" // Since ActActivitySummary tracks workouts
                    })
                    .ToListAsync();

                _logger.LogInformation("✅ ADMIN: Retrieved {Count} activity logs (Total: {Total})", logs.Count, total);

                return Ok(new
                {
                    data = logs,
                    total = total,
                    page = page,
                    pageSize = pageSize,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ADMIN: Error fetching activity logs");
                return StatusCode(500, new { error = "An error occurred while fetching activity logs." });
            }
        }

        /// <summary>
        /// GET: api/actlogs/admin/{id}
        /// </summary>
        [HttpGet("admin/{id}")]
        public async Task<ActionResult<object>> AdminGetLog(int id)
        {
            try
            {
                _logger.LogInformation("📡 ADMIN: Fetching activity log ID: {Id}", id);

                var log = await (from a in _context.ActActivitySummaries
                                 join u in _context.UsrUsers on a.UserId equals u.UserId
                                 where a.SummaryId == id
                                 select new
                                 {
                                     a.SummaryId,
                                     a.UserId,
                                     u.Username,
                                     u.Email,
                                     a.CaloriesBurned,
                                     a.TotalMinutes,
                                     a.LogDate,
                                     a.UpdatedAt,
                                     ActivityType = "Workout"
                                 })
                                 .FirstOrDefaultAsync();

                if (log == null)
                {
                    _logger.LogWarning("❌ ADMIN: Activity log not found: {Id}", id);
                    return NotFound(new { error = $"Activity log with ID {id} not found." });
                }

                return Ok(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ADMIN: Error fetching activity log ID: {Id}", id);
                return StatusCode(500, new { error = "An error occurred while fetching the activity log." });
            }
        }

        /// <summary>
        /// DELETE: api/actlogs/admin/{id}
        /// </summary>
        [HttpDelete("admin/{id}")]
        public async Task<IActionResult> AdminDeleteLog(int id)
        {
            try
            {
                _logger.LogInformation("📡 ADMIN: Deleting activity log ID: {Id}", id);

                var log = await _context.ActActivitySummaries
                    .FirstOrDefaultAsync(a => a.SummaryId == id);

                if (log == null)
                {
                    _logger.LogWarning("❌ ADMIN: Activity log not found: {Id}", id);
                    return NotFound(new { error = $"Activity log with ID {id} not found." });
                }

                _context.ActActivitySummaries.Remove(log);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ ADMIN: Activity log deleted: {Id}", id);
                return Ok(new { message = "Activity log deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ADMIN: Error deleting activity log ID: {Id}", id);
                return StatusCode(500, new { error = "An error occurred while deleting the activity log." });
            }
        }

        #endregion
    }
}