using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.Data.SqlClient;
using System.Data;

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

                // ✅ RAW SQL - UNION of Workout, Nutrition, Water logs
                var sql = @"
                    SELECT 
                        a.user_id,
                        u.username,
                        u.email,
                        a.activity_type,
                        a.activity_date,
                        a.details
                    FROM (
                        -- 1. WORKOUT SESSIONS
                        SELECT 
                            s.user_id,
                            'Workout' AS activity_type,
                            s.completed_at AS activity_date,
                            CONCAT('Completed workout: ', w.workout_name, ' (Day ', s.workout_day, ')') AS details
                        FROM usr_user_workout_sessions s
                        INNER JOIN usr_user_session_workouts sw ON s.session_id = sw.session_id
                        INNER JOIN wrk_workouts w ON sw.workout_id = w.workout_id
                        WHERE s.status = 'COMPLETED'

                        UNION ALL

                        -- 2. NUTRITION DAILY LOGS
                        SELECT 
                            d.user_id,
                            'Nutrition' AS activity_type,
                            d.plan_date AS activity_date,
                            CONCAT('Logged meals: ', d.calories_consumed, ' kcal consumed, ', d.calories_burned, ' kcal burned') AS details
                        FROM ntr_daily_logs d
                        WHERE d.marked_done_at IS NOT NULL

                        UNION ALL

                        -- 3. WATER LOGS
                        SELECT 
                            w.user_id,
                            'Water' AS activity_type,
                            w.log_date AS activity_date,
                            CONCAT('Logged ', w.water_ml, ' ml water') AS details
                        FROM ntr_water_logs w
                    ) a
                    INNER JOIN usr_users u ON a.user_id = u.user_id
                    WHERE 1=1
                ";

                var parameters = new List<SqlParameter>();

                // Apply filters (parameterized - SAFE)
                if (!string.IsNullOrEmpty(search))
                {
                    sql += " AND (u.username LIKE @search OR u.email LIKE @search OR a.details LIKE @search)";
                    parameters.Add(new SqlParameter("@search", $"%{search}%"));
                }

                if (fromDate.HasValue)
                {
                    sql += " AND a.activity_date >= @fromDate";
                    parameters.Add(new SqlParameter("@fromDate", fromDate.Value));
                }

                if (toDate.HasValue)
                {
                    sql += " AND a.activity_date <= @toDate";
                    parameters.Add(new SqlParameter("@toDate", toDate.Value));
                }

                // Count query - for pagination
                var countSql = $@"
                    SELECT COUNT(*) AS Value
                    FROM (
                        SELECT s.user_id, s.completed_at AS activity_date
                        FROM usr_user_workout_sessions s
                        INNER JOIN usr_user_session_workouts sw ON s.session_id = sw.session_id
                        INNER JOIN wrk_workouts w ON sw.workout_id = w.workout_id
                        WHERE s.status = 'COMPLETED'
                        UNION ALL
                        SELECT d.user_id, d.plan_date AS activity_date
                        FROM ntr_daily_logs d
                        WHERE d.marked_done_at IS NOT NULL
                        UNION ALL
                        SELECT w.user_id, w.log_date AS activity_date
                        FROM ntr_water_logs w
                    ) a
                    INNER JOIN usr_users u ON a.user_id = u.user_id
                    WHERE 1=1
                ";

                // Apply same filters to count query
                if (!string.IsNullOrEmpty(search))
                {
                    countSql += " AND (u.username LIKE @search OR u.email LIKE @search)";
                }
                if (fromDate.HasValue)
                {
                    countSql += " AND a.activity_date >= @fromDate";
                }
                if (toDate.HasValue)
                {
                    countSql += " AND a.activity_date <= @toDate";
                }

                var total = await _context.Database
                    .SqlQueryRaw<int>(countSql, parameters.ToArray())
                    .FirstOrDefaultAsync();

                // Pagination
                sql += " ORDER BY a.activity_date DESC OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";
                parameters.Add(new SqlParameter("@offset", (page - 1) * pageSize));
                parameters.Add(new SqlParameter("@pageSize", pageSize));

                // ✅ Execute raw SQL and map to DTO
                var logs = await _context.Database
                    .SqlQueryRaw<ActivityLogDto>(sql, parameters.ToArray())
                    .ToListAsync();

                _logger.LogInformation($"✅ Retrieved {logs.Count} logs, Total: {total}");

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
                _logger.LogError(ex, "❌ Error fetching activity logs");
                return StatusCode(500, new { 
                    error = "An error occurred while fetching activity logs.",
                    details = ex.Message,
                    stackTrace = ex.StackTrace 
                });
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
                _logger.LogInformation($"📡 ADMIN: Fetching activity log for user ID: {id}");

                var user = await _context.UsrUsers
                    .FirstOrDefaultAsync(u => u.UserId == id);

                if (user == null)
                {
                    return NotFound(new { error = $"User with ID {id} not found." });
                }

                return Ok(new
                {
                    UserId = user.UserId,
                    Username = user.Username,
                    Email = user.Email,
                    ActivityType = "User Activity",
                    ActivityDate = DateTime.UtcNow,
                    Details = "User activity log"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error fetching activity log for user: {id}");
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
                _logger.LogInformation($"📡 ADMIN: Deleting activity logs for user ID: {id}");

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

                _logger.LogInformation($"✅ Activity logs deleted for user ID: {id}");
                return Ok(new { message = "Activity logs deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error deleting activity logs for user: {id}");
                return StatusCode(500, new { error = "An error occurred while deleting the activity logs." });
            }
        }

        #endregion
    }
}