using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Entities;
using FlexiFit.Api.Dtos; 

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

                // ✅ REPLICATE THE OLD UNION LOGIC
                // 1. Workout sessions (each workout item becomes one row)
                var workoutLogs = from s in _context.UsrUserWorkoutSessions
                                  join sw in _context.UsrUserSessionWorkouts on s.SessionId equals sw.SessionId
                                  join w in _context.WrkWorkouts on sw.WorkoutId equals w.WorkoutId
                                  where s.Status == "COMPLETED"
                                  select new
                                  {
                                      s.UserId,
                                      ActivityType = "Workout",
                                      ActivityDate = s.CompletedAt ?? DateTime.UtcNow,
                                      Details = $"Completed workout: {w.WorkoutName} (Day {s.WorkoutDay})"
                                  };

                // 2. Nutrition daily logs (when marked done)
                var nutritionLogs = from d in _context.NtrDailyLogs
                                    where d.MarkedDoneAt != null
                                    select new
                                    {
                                        d.UserId,
                                        ActivityType = "Nutrition",
                                        ActivityDate = d.PlanDate.ToDateTime(TimeOnly.MinValue), // ✅ DateOnly → DateTime
                                        Details = $"Logged meals: {d.CaloriesConsumed} kcal consumed, {d.CaloriesBurned} kcal burned"
                                    };

                // 3. Water logs
                var waterLogs = from w in _context.NtrWaterLogs
                                select new
                                {
                                    w.UserId,
                                    ActivityType = "Water",
                                    ActivityDate = w.LogDate.ToDateTime(TimeOnly.MinValue), // ✅ DateOnly → DateTime
                                    Details = $"Logged {w.WaterMl} ml water"
                                };

                // Combine all logs (UNION ALL)
                var allLogs = workoutLogs
                    .Concat(nutritionLogs)
                    .Concat(waterLogs)
                    .Join(_context.UsrUsers, a => a.UserId, u => u.UserId, (a, u) => new
                    {
                        a.UserId,
                        u.Username,
                        u.Email,
                        a.ActivityType,
                        a.ActivityDate,
                        a.Details
                    });

                // Apply filters
                if (!string.IsNullOrEmpty(search))
                {
                    allLogs = allLogs.Where(l =>
                        (l.Username ?? "").Contains(search) ||
                        (l.Email ?? "").Contains(search) ||
                        (l.Details ?? "").Contains(search));
                }

                if (fromDate.HasValue)
                {
                    var from = fromDate.Value.Date;
                    allLogs = allLogs.Where(l => l.ActivityDate >= from);
                }

                if (toDate.HasValue)
                {
                    var to = toDate.Value.Date.AddDays(1).AddTicks(-1);
                    allLogs = allLogs.Where(l => l.ActivityDate <= to);
                }

                // Count total
                var total = await allLogs.CountAsync();

                // Pagination
                var logs = await allLogs
                    .OrderByDescending(l => l.ActivityDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(l => new ActivityLogDto
                    {
                        user_id = l.UserId,
                        username = l.Username ?? "",
                        email = l.Email ?? "",
                        activity_type = l.ActivityType,
                        activity_date = l.ActivityDate,
                        details = l.Details
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
                _logger.LogInformation("📡 ADMIN: Fetching activity log for user ID: {Id}", id);

                var user = await _context.UsrUsers
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    return NotFound(new { error = $"User with ID {id} not found." });
                }

                // Return basic user info (simplified)
                var log = new
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Email = user.Email,
                    ActivityType = "User Activity",
                    ActivityDate = DateTime.UtcNow,
                    Details = "User activity log"
                };

                return Ok(log);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ ADMIN: Error fetching activity log for user: {Id}", id);
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
                _logger.LogInformation("📡 ADMIN: Deleting activity logs for user ID: {Id}", id);

                var user = await _context.UsrUsers
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    return NotFound(new { error = $"User with ID {id} not found." });
                }

                // Delete workout sessions
                var workoutSessions = await _context.UsrUserWorkoutSessions
                    .Where(s => s.UserId == id && s.Status == "COMPLETED")
                    .ToListAsync();
                _context.UsrUserWorkoutSessions.RemoveRange(workoutSessions);

                // Delete nutrition logs
                var nutritionLogs = await _context.NtrDailyLogs
                    .Where(d => d.UserId == id && d.MarkedDoneAt != null)
                    .ToListAsync();
                _context.NtrDailyLogs.RemoveRange(nutritionLogs);

                // Delete water logs
                var waterLogs = await _context.NtrWaterLogs
                    .Where(w => w.UserId == id)
                    .ToListAsync();
                _context.NtrWaterLogs.RemoveRange(waterLogs);

                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Activity logs deleted for user ID: {Id}", id);
                return Ok(new { message = "Activity logs deleted successfully." });
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