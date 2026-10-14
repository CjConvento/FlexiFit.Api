using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlexiFit.Api.Services;

public class ActivityLogService
{
    private readonly FlexiFitDbContext _context;

    public ActivityLogService(FlexiFitDbContext context)
    {
        _context = context;
    }

    // ==========================================
    // ACTIVITY LOGGING LOGIC
    // ==========================================
    public async Task<object> GetAllLogsAsync(string? search, DateTime? fromDate, DateTime? toDate, int page, int pageSize)
    {
        // Raw SQL is the best choice for this complex UNION query
        var sql = @"
            SELECT 
                a.user_id, u.username, u.email, a.activity_type, a.activity_date, a.details
            FROM (
                SELECT s.user_id, 'Workout' AS activity_type, s.completed_at AS activity_date,
                       CONCAT('Completed workout: ', w.workout_name, ' (Day ', s.workout_day, ')') AS details
                FROM usr_user_workout_sessions s
                INNER JOIN usr_user_session_workouts sw ON s.session_id = sw.session_id
                INNER JOIN wrk_workouts w ON sw.workout_id = w.workout_id
                WHERE s.status = 'COMPLETED'
                UNION ALL
                SELECT d.user_id, 'Nutrition' AS activity_type, d.plan_date AS activity_date,
                       CONCAT('Logged meals: ', d.calories_consumed, ' kcal consumed, ', d.calories_burned, ' kcal burned') AS details
                FROM ntr_daily_logs d
                WHERE d.marked_done_at IS NOT NULL
                UNION ALL
                SELECT w.user_id, 'Water' AS activity_type, w.log_date AS activity_date,
                       CONCAT('Logged ', w.water_ml, ' ml water') AS details
                FROM ntr_water_logs w
            ) a
            INNER JOIN usr_users u ON a.user_id = u.user_id
            WHERE 1=1
        ";

        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(search))
        {
            sql += " AND (u.username ILIKE @search OR u.email ILIKE @search OR a.details ILIKE @search)";
            parameters.Add(new NpgsqlParameter("@search", $"%{search}%"));
        }
        if (fromDate.HasValue)
        {
            sql += " AND a.activity_date >= @fromDate";
            parameters.Add(new NpgsqlParameter("@fromDate", fromDate.Value));
        }
        if (toDate.HasValue)
        {
            sql += " AND a.activity_date <= @toDate";
            parameters.Add(new NpgsqlParameter("@toDate", toDate.Value));
        }

        // Count query for pagination
        var countSql = $@"
            SELECT COUNT(*) AS Value
            FROM (
                SELECT s.user_id, s.completed_at AS activity_date FROM usr_user_workout_sessions s
                INNER JOIN usr_user_session_workouts sw ON s.session_id = sw.session_id
                INNER JOIN wrk_workouts w ON sw.workout_id = w.workout_id WHERE s.status = 'COMPLETED'
                UNION ALL
                SELECT d.user_id, d.plan_date AS activity_date FROM ntr_daily_logs d WHERE d.marked_done_at IS NOT NULL
                UNION ALL
                SELECT w.user_id, w.log_date AS activity_date FROM ntr_water_logs w
            ) a
            INNER JOIN usr_users u ON a.user_id = u.user_id
            WHERE 1=1
        ";

        if (!string.IsNullOrEmpty(search)) countSql += " AND (u.username ILIKE @search OR u.email ILIKE @search)";
        if (fromDate.HasValue) countSql += " AND a.activity_date >= @fromDate";
        if (toDate.HasValue) countSql += " AND a.activity_date <= @toDate";

        var total = await _context.Database
            .SqlQueryRaw<int>(countSql, parameters.ToArray())
            .FirstOrDefaultAsync();

        // Pagination
        sql += " ORDER BY a.activity_date DESC LIMIT @pageSize OFFSET @offset";
        parameters.Add(new NpgsqlParameter("@offset", (page - 1) * pageSize));
        parameters.Add(new NpgsqlParameter("@pageSize", pageSize));

        var logs = await _context.Database
            .SqlQueryRaw<ActivityLogDto>(sql, parameters.ToArray())
            .ToListAsync();

        return new
        {
            data = logs,
            total = total,
            page = page,
            pageSize = pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<object> GetLogByUserIdAsync(int id)
    {
        var user = await _context.UsrUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == id);

        // Throw KeyNotFoundException para automatic na maging 404 sa Middleware
        if (user == null)
            throw new KeyNotFoundException($"User with ID {id} not found.");

        return new
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email,
            ActivityType = "User Activity",
            ActivityDate = DateTime.UtcNow,
            Details = "User activity log"
        };
    }

    public async Task DeleteLogsByUserIdAsync(int id)
    {
        var user = await _context.UsrUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == id);

        if (user == null)
            throw new KeyNotFoundException($"User with ID {id} not found.");

        // Direct SQL DELETE
        
        // 1. Get session IDs first (lightweight)
        var sessionIds = await _context.UsrUserWorkoutSessions
            .Where(s => s.UserId == id && s.Status == "COMPLETED")
            .Select(s => s.SessionId)
            .ToListAsync();

        if (sessionIds.Any())
        {
            // Direct delete of children, then parents
            await _context.UsrUserSessionWorkouts.Where(x => sessionIds.Contains(x.SessionId)).ExecuteDeleteAsync();
            await _context.UsrUserWorkoutSessions.Where(x => x.UserId == id && x.Status == "COMPLETED").ExecuteDeleteAsync();
        }

        // 2. Direct delete of other logs
        await _context.NtrDailyLogs.Where(d => d.UserId == id && d.MarkedDoneAt != null).ExecuteDeleteAsync();
        await _context.NtrWaterLogs.Where(w => w.UserId == id).ExecuteDeleteAsync();
    }
}