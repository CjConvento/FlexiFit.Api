using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

/// <summary>
/// Single source of truth for "which main workouts does a user get for a given
/// program / week / day type".
///
/// Used by:
///   - MobileService.SubmitOnboardingProfileAsync (Day 1 seeding)
///   - WorkoutPlanGenerator.GenerateForSessionAsync (lazy daily generation)
///   - ProgramService.GenerateProgramSlotsForNewCycleAsync (level-up regeneration)
///
/// Rules (in order):
///   1. Pool = template rows for (programId, weekNo, dayType).
///   2. Level filter (case-insensitive). If it matches nothing, fall back to the whole pool.
///   3. De-duplicate by WorkoutId (the template can contain repeated rows).
///   4. Shuffle (or keep template order when shuffle = false).
///   5. Take up to MaxMainWorkoutsPerDay; once MinMainWorkoutsPerDay is reached,
///      stop early when the calorie-burn target is met.
/// </summary>
public class WorkoutPoolSelector
{
    public const int MinMainWorkoutsPerDay = 4;
    public const int MaxMainWorkoutsPerDay = 5;

    private readonly FlexiFitDbContext _db;

    public WorkoutPoolSelector(FlexiFitDbContext db)
    {
        _db = db;
    }

    // ── DB access ────────────────────────────────────────────────

    /// <summary>
    /// Loads the raw template pool (with Workout loaded) for one program / week / day type.
    /// </summary>
    public async Task<List<WrkProgramTemplateDaytypeWorkout>> GetPoolAsync(int programId, int weekNo, string dayType)
    {
        return await _db.WrkProgramTemplateDaytypeWorkouts
            .AsNoTracking()
            .Include(dw => dw.Workout)
            .Where(dw => dw.ProgramId == programId
                      && dw.WeekNo == weekNo
                      && dw.DayType == dayType)
            .ToListAsync();
    }

    /// <summary>
    /// Calories a single session should aim to burn, from the user's latest calorie
    /// target and onboarding activity level.
    /// </summary>
    public async Task<double> GetTargetBurnAsync(int userId)
    {
        double dailyTarget = await _db.UsrUserMetrics
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.RecordedAt)
            .Select(m => (double?)m.CalorieTarget)
            .FirstOrDefaultAsync() ?? 2000;

        var activityLevel = await _db.UsrUserOnboardingDetails
            .Where(o => o.UserId == userId)
            .Select(o => o.ActivityLevel)
            .FirstOrDefaultAsync();

        return CalculateTargetBurn(dailyTarget, activityLevel);
    }

    // ── Pure logic (no DB) ───────────────────────────────────────

    public static double CalculateTargetBurn(double dailyCalorieTarget, string? activityLevel)
    {
        string level = (activityLevel ?? "Sedentary").ToLower().Replace(" ", "").Replace("_", "");

        double multiplier = level switch
        {
            "sedentary"     => 0.12,
            "lightlyactive" => 0.18,
            "active"        => 0.22,
            "veryactive"    => 0.28,
            _               => 0.15
        };

        return dailyCalorieTarget * multiplier;
    }

    /// <summary>
    /// Picks the day's main workouts from a pool. Never returns the same WorkoutId twice.
    /// </summary>
    /// <param name="pool">Template rows with Workout loaded.</param>
    /// <param name="allowedLevels">Accepted difficulty levels. Null/empty = no level filter.</param>
    /// <param name="targetBurn">Calorie target for the session. Null = ignore calories (count cap only).</param>
    /// <param name="shuffle">true = random draw from the pool, false = template (WorkoutOrder) order.</param>
    public static List<WrkProgramTemplateDaytypeWorkout> Pick(
        IEnumerable<WrkProgramTemplateDaytypeWorkout> pool,
        IEnumerable<string>? allowedLevels = null,
        double? targetBurn = null,
        bool shuffle = true,
        ILogger? logger = null)
    {
        var poolList = pool.ToList();

        var levels = (allowedLevels ?? Enumerable.Empty<string>())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(NormalizeLevel)
            .ToHashSet();

        var candidates = levels.Count == 0
            ? poolList
            : poolList
                .Where(dw => levels.Contains(NormalizeLevel(dw.Workout?.DifficultyLevel ?? "Beginner")))
                .ToList();

        if (candidates.Count == 0 && poolList.Count > 0)
        {
            logger?.LogWarning(
                "[POOL] Level filter [{Levels}] matched 0 of {Count} template rows. Using the whole pool.",
                string.Join(", ", levels), poolList.Count);
            candidates = poolList;
        }

        // One row per exercise; keep the earliest template slot.
        var unique = candidates
            .GroupBy(dw => dw.WorkoutId)
            .Select(g => g.OrderBy(dw => dw.WorkoutOrder).First());

        var ordered = shuffle
            ? unique.OrderBy(_ => Random.Shared.Next())
            : unique.OrderBy(dw => dw.WorkoutOrder);

        var picked = new List<WrkProgramTemplateDaytypeWorkout>();
        double burn = 0;

        foreach (var dw in ordered)
        {
            if (picked.Count >= MaxMainWorkoutsPerDay) break;

            if (targetBurn.HasValue
                && picked.Count >= MinMainWorkoutsPerDay
                && burn >= targetBurn.Value) break;

            picked.Add(dw);
            burn += (double)(dw.Workout?.CaloriesBurned ?? 0);
        }

        return picked;
    }

    /// <summary>
    /// Maps picked template rows to session workouts with sequential OrderNo.
    /// </summary>
    public static List<UsrUserSessionWorkout> BuildSessionWorkouts(
        int sessionId,
        IReadOnlyList<WrkProgramTemplateDaytypeWorkout> picked,
        int startOrder = 1)
    {
        return picked
            .Select((dw, i) => new UsrUserSessionWorkout
            {
                SessionId = sessionId,
                WorkoutId = dw.WorkoutId,
                Sets = dw.SetsDefault,
                Reps = dw.RepsDefault,
                LoadKg = 0,
                OrderNo = startOrder + i
            })
            .ToList();
    }

    private static string NormalizeLevel(string level) => level.Trim().ToLowerInvariant();
}
