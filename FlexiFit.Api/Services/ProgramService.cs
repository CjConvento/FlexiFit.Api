using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class ProgramService
{
    private readonly FlexiFitDbContext _context;

    public ProgramService(FlexiFitDbContext context) => _context = context;

    /// <summary>
    /// Gets all ACTIVE program instances for a user, ordered by creation date.
    /// </summary>
    public async Task<List<UsrUserProgramInstance>> GetActiveProgramsAsync(int userId)
    {
        return await _context.UsrUserProgramInstances
            .Where(p => p.UserId == userId && p.Status == "ACTIVE")
            .OrderBy(p => p.CreatedAt)
            .ThenBy(p => p.InstanceId)
            .Include(p => p.Program)
            .ToListAsync();
    }

    /// <summary>
    /// Calculates the 1-based ordinal number of a specific program instance.
    /// </summary>
    public async Task<int> GetProgramNumberAsync(int userId, int instanceId)
    {
        var activePrograms = await GetActiveProgramsAsync(userId);
        int index = activePrograms.FindIndex(p => p.InstanceId == instanceId);
        return index >= 0 ? index + 1 : 1;
    }

    /// <summary>
    /// Gets the total count of ACTIVE programs for a user.
    /// </summary>
    public async Task<int> GetTotalActiveProgramsAsync(int userId)
    {
        return await _context.UsrUserProgramInstances
            .CountAsync(p => p.UserId == userId && p.Status == "ACTIVE");
    }
}