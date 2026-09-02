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
        public async Task<ActionResult<IEnumerable<object>>> AdminGetAllLogs(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                _logger.LogInformation("📡 ADMIN: Fetching all activity logs");

                // Base query - union of all activity types
                var query = _context.ActivityLogsView
                    .AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(a =>
                        a.Username.Contains(search) ||
                        a.Email.Contains(search) ||
                        a.Details.Contains(search));
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(a => a.ActivityDate >= fromDate.Value);
                }

                if (toDate.HasValue)
                {
                    query = query.Where(a => a.ActivityDate <= toDate.Value);
                }

                var total = await query.CountAsync();

                var logs = await query
                    .OrderByDescending(a => a.ActivityDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
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

                var log = await _context.ActivityLogsView
                    .FirstOrDefaultAsync(a => a.UserId == id);

                if (log == null)
                {
                    _logger.LogWarning("❌ ADMIN: Activity log not found for user: {Id}", id);
                    return NotFound(new { error = $"Activity log with user ID {id} not found." });
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

                var log = await _context.ActivityLogsView
                    .FirstOrDefaultAsync(a => a.UserId == id);

                if (log == null)
                {
                    _logger.LogWarning("❌ ADMIN: Activity log not found for user: {Id}", id);
                    return NotFound(new { error = $"Activity log with ID {id} not found." });
                }

                // Delete related records (workout sessions, nutrition logs, water logs)
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // 1. Delete workout sessions
                    var workoutSessions = await _context.UsrUserWorkoutSessions
                        .Where(s => s.UserId == id && s.Status == "COMPLETED")
                        .ToListAsync();
                    _context.UsrUserWorkoutSessions.RemoveRange(workoutSessions);

                    // 2. Delete nutrition logs
                    var nutritionLogs = await _context.NtrDailyLogs
                        .Where(d => d.UserId == id && d.MarkedDoneAt != null)
                        .ToListAsync();
                    _context.NtrDailyLogs.RemoveRange(nutritionLogs);

                    // 3. Delete water logs
                    var waterLogs = await _context.NtrWaterLogs
                        .Where(w => w.UserId == id)
                        .ToListAsync();
                    _context.NtrWaterLogs.RemoveRange(waterLogs);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogInformation("✅ ADMIN: Activity logs deleted for user: {Id}", id);
                    return Ok(new { message = "Activity logs deleted successfully." });
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ADMIN: Error deleting activity logs for user: {Id}", id);
                return StatusCode(500, new { error = "An error occurred while deleting the activity logs." });
            }
        }

        #endregion
    }
}   