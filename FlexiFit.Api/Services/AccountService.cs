using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace FlexiFit.Api.Services;

public class AccountService
{
    private readonly FlexiFitDbContext _context;

    public AccountService(
        FlexiFitDbContext context
    )
    {
        _context = context;
    }

    // Helper: Resolve UserId from Claims (with DB fallback)
    public async Task<int?> ResolveUserIdFromClaimsAsync(ClaimsPrincipal user)
    {
        var firebaseUid = user.FindFirst("user_id")?.Value ?? user.FindFirst("uid")?.Value;
        var email = user.FindFirst(ClaimTypes.Email)?.Value;

        if (!string.IsNullOrWhiteSpace(firebaseUid))
        {
            var dbUser = await _context.UsrUsers.FirstOrDefaultAsync(x => x.FirebaseUid == firebaseUid);
            if (dbUser != null) return dbUser.UserId;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var dbUser = await _context.UsrUsers.FirstOrDefaultAsync(x => x.Email == email);
            if (dbUser != null) return dbUser.UserId;
        }

        return null;
    }

    // 1. Reset Progress (Gamit ang ExecuteDeleteAsync para sa maximum performance)
    public async Task ResetProgressAsync(int userId)
    {
        // Ginamit ang Transaction para siguradong all-or-nothing ang pag-reset
        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Kunin lang ang IDs, hindi ang buong entity (Memory Efficient)
            var sessionIds = await _context.UsrUserWorkoutSessions
                .Where(s => s.UserId == userId)
                .Select(s => s.SessionId)
                .ToListAsync();

            if (sessionIds.Any())
            {
                // 2. STRICT ORDER: Burahin ang CHILDREN muna
                await _context.UsrUserSessionWorkouts
                    .Where(x => sessionIds.Contains(x.SessionId))
                    .ExecuteDeleteAsync();

                await _context.UsrUserSessionInstances
                    .Where(x => sessionIds.Contains(x.SessionId))
                    .ExecuteDeleteAsync();

                // 3. Burahin ang PARENT (Sessions)
                await _context.UsrUserWorkoutSessions
                    .Where(x => x.UserId == userId)
                    .ExecuteDeleteAsync();
            }

            // 4. Burahin ang ibang independent logs 
            // (Hindi na kailangan ng strict order sa pagitan nila kasi walang FK dependency sa isa't isa, 
            //  pero organized pa rin ang pagkakasunod)
            await _context.UsrUserMetrics
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await _context.DailyProgressLogs
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await _context.NtrWaterLogs
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            await _context.UsrUserGeneralAchievements
                .Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();

            // 5. Commit ang lahat ng changes nang sabay-sabay
            await tx.CommitAsync();
        }
        catch
        {
            // Rollback kung may nag-fail sa gitna para iwas corrupted data
            await tx.RollbackAsync();
            throw; // Let Global Exception Middleware handle it
        }
        finally
        {
            await tx.DisposeAsync();
        }
    }

    // 2. Update Email
    public async Task UpdateEmailAsync(int userId, string newEmail)
    {
        await _context.UsrUsers
            .Where(u => u.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.Email, newEmail)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow));
    }

    // 3. Link Google
    public async Task LinkGoogleAsync(int userId, string newEmail, string newFirebaseUid)
    {
        await _context.UsrUsers
            .Where(u => u.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.Email, newEmail)
                .SetProperty(u => u.FirebaseUid, newFirebaseUid)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow));
    }

    // 4. Delete account (Cascade delete via EF Core)
    public async Task DeleteAccountAsync(int userId)
    {
        // Direct delete. Kung may cascade delete sa DB, lilinis na 'to ng lahat.
        await _context.UsrUsers
            .Where(u => u.UserId == userId)
            .ExecuteDeleteAsync();
    }

    // 5. Export Data
    public async Task<object> ExportDataAsync(int userId)
    {
        // Kunin lahat ng data mula sa iba't ibang tables
        return new
        {
            Profile = await _context.UsrUsers
                .Where(u => u.UserId == userId)
                .Select(u => new { u.Username, u.Email, u.CreatedAt })
                .FirstOrDefaultAsync(),

            Metrics = await _context.UsrUserMetrics
                .Where(m => m.UserId == userId).ToListAsync(),

            WorkoutSessions = await _context.UsrUserWorkoutSessions
                .Where(w => w.UserId == userId).ToListAsync(),

            WaterLogs = await _context.NtrWaterLogs
                .Where(w => w.UserId == userId).ToListAsync(),

            DailyLogs = await _context.DailyProgressLogs
                .Where(d => d.UserId == userId).ToListAsync(),

            Achievements = await _context.UsrUserGeneralAchievements
                .Where(a => a.UserId == userId).ToListAsync()
        };
    }
}