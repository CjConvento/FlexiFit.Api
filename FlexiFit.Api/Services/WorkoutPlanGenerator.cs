using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class WorkoutPlanGenerator
{
    private readonly FlexiFitDbContext _db;
    private readonly ILogger<WorkoutPlanGenerator> _logger;
    private readonly WorkoutPoolSelector _poolSelector;
    
    public WorkoutPlanGenerator(
        FlexiFitDbContext db,
        ILogger<WorkoutPlanGenerator> logger,
        WorkoutPoolSelector workoutPoolSelector)
    {
        _db = db;
        _logger = logger;
        _poolSelector = workoutPoolSelector;
    }

        /// <summary>
        /// Generates workout exercises for a session.
        /// </summary>
        /// <param name="userId">
        /// When provided, the calorie-burn target is applied to the main-workout pick
        /// (same rule as onboarding). When null, only the 4-5 count cap applies.
        /// </param>
        /// <returns>List of created UsrUserSessionWorkout entities (NOT saved yet)</returns>
        public async Task<List<UsrUserSessionWorkout>> GenerateForSessionAsync(
            int sessionId,
            int programId,
            string dayType,
            int weekNo,
            string fitnessLevel,
            bool isRehab,
            int? userId = null) 
        {
            _logger.LogInformation($"Generating workout plan: Session={sessionId}, Program={programId}, DayType={dayType}, Week={weekNo}, Level={fitnessLevel}, Rehab={isRehab}");

            // Check if workouts already exist for this session
            var existingWorkouts = await _db.UsrUserSessionWorkouts
                .AnyAsync(sw => sw.SessionId == sessionId);
            
            if (existingWorkouts)
            {
                _logger.LogInformation($"Session {sessionId} already has workouts. Skipping generation.");
                return new List<UsrUserSessionWorkout>();
            }

            // Define allowed difficulties
            var allowedDifficulties = new List<string> { fitnessLevel };
            if (isRehab)
                allowedDifficulties.Add("REHAB LEVEL");

            var sessionWorkouts = new List<UsrUserSessionWorkout>();
            int order = 1;

            // 1. Add 2 random warmups filtered by allowed difficulties
            var warmups = await _db.WrkWorkouts
                .Where(w => w.Category == "WARMUP"
                        && w.IsActive == true
                        && allowedDifficulties.Contains(w.DifficultyLevel!))
                .OrderBy(x => EF.Functions.Random())
                .Take(2)
                .ToListAsync();

            foreach (var warmup in warmups)
            {
                sessionWorkouts.Add(new UsrUserSessionWorkout
                {
                    SessionId = sessionId,
                    WorkoutId = warmup.WorkoutId,
                    Sets = 2,
                    Reps = 12,
                    OrderNo = order++,
                    LoadKg = 0
                });
            }

            // 2. Add main workouts from the program/week/day-type pool.
            //    Level filter, de-dupe, 4-5 cap and calorie stop live in WorkoutPoolSelector.
            // Replaced old manual .Take(8) logic with WorkoutPoolSelector
            var pool = await _poolSelector.GetPoolAsync(programId, weekNo, dayType);
            double? targetBurn = userId.HasValue
                ? await _poolSelector.GetTargetBurnAsync(userId.Value)
                : null;

            var picked = WorkoutPoolSelector.Pick(pool, allowedDifficulties, targetBurn, logger: _logger);
            
            if (!picked.Any())
            {
                // No template rows at all for this program/week/day type
                _logger.LogWarning($"No template workouts found, using random fallback");
                var fallbackWorkouts = await _db.WrkWorkouts
                    .Where(w => w.IsActive == true
                    && w.Category != null
                    && w.Category.ToUpper() != "WARMUP"
                    && w.Category.ToUpper() != "CARDIO")
                .OrderBy(x => EF.Functions.Random())
                .Take(WorkoutPoolSelector.MaxMainWorkoutsPerDay) // uses 5
                .ToListAsync();

                foreach (var w in fallbackWorkouts)
                {
                    sessionWorkouts.Add(new UsrUserSessionWorkout
                    {
                        SessionId = sessionId,
                        WorkoutId = w.WorkoutId,
                        Sets = 3,
                        Reps = 10,
                        OrderNo = order++,
                        LoadKg = 0
                    });
                }
            }
            else
            {
                sessionWorkouts.AddRange(WorkoutPoolSelector.BuildSessionWorkouts(sessionId, picked, startOrder: order));
                order += picked.Count;
            }
                
            // Ensure no duplicate order numbers
            var duplicateOrders = sessionWorkouts
                .GroupBy(w => w.OrderNo)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateOrders.Any())
            {
                _logger.LogWarning($"Found duplicate order numbers, re-sequencing");
                // Fixed infinite loop bug (changed '1 <' to 'i <')
                for (int i = 0; i < sessionWorkouts.Count; i++)
                    sessionWorkouts[i].OrderNo = i + 1;
            }

            _logger.LogInformation($"Generated {sessionWorkouts.Count} exercises for Session {sessionId} (Warmups: {warmups.Count}, Main: {sessionWorkouts.Count - warmups.Count})");
            return sessionWorkouts;
        }

        /// <summary>
        /// Ensures warmups exist for a session (separate method for repair scenarios).
        /// </summary>
        public async Task<List<UsrUserSessionWorkout>> EnsureWarmupsAsync(
            int sessionId,
            int programId,
            string dayType,
            int weekNo,
            string fitnessLevel,
            bool isRehab)
        {
            // Build allowed difficulties
            var allowedDifficulties = new List<string> { fitnessLevel };
            if (isRehab)
                allowedDifficulties.Add("REHAB LEVEL");

            // Check if session already has any warmups
            bool hasWarmups = await _db.UsrUserSessionWorkouts
                .Include(sw => sw.Workout)
                .AnyAsync(sw => sw.SessionId == sessionId && sw.Workout != null && sw.Workout.Category == "WARMUP");
            
            if (hasWarmups)
            {
                _logger.LogInformation($"Session {sessionId} already has warmups");
                return new List<UsrUserSessionWorkout>();
            }

            // If not, add 2 random warmups filtered by allowed difficulties
            var warmups = await _db.WrkWorkouts
                .Where(w => w.Category == "WARMUP"
                        && w.IsActive == true
                        && allowedDifficulties.Contains(w.DifficultyLevel!))
                .OrderBy(x => EF.Functions.Random())
                .Take(2)
                .ToListAsync();

            if (!warmups.Any())
            {
                _logger.LogWarning("No active warmups found for the allowed difficulty levels");
                return new List<UsrUserSessionWorkout>();
            }

            // Get the current max OrderNo
            int maxOrder = await _db.UsrUserSessionWorkouts
                .Where(sw => sw.SessionId == sessionId)
                .MaxAsync(sw => (int?)sw.OrderNo) ?? 0;

            var newWarmups = new List<UsrUserSessionWorkout>();
            int order = maxOrder + 1;

            foreach (var warmup in warmups)
            {
                newWarmups.Add(new UsrUserSessionWorkout
                {
                    SessionId = sessionId,
                    WorkoutId = warmup.WorkoutId,
                    Sets = 2,
                    Reps = 12,
                    OrderNo = order++,
                    LoadKg = 0
                });
            }

            _logger.LogInformation($"Added {newWarmups.Count} warmups to session {sessionId}");
            return newWarmups;
        }

        /// <summary>
        /// Reorders all workouts in a session to ensure warmups come first.
        /// </summary>
        public async Task ReorderWorkoutsAsync(int sessionId)
        {
            var allWorkouts = await _db.UsrUserSessionWorkouts
                .Include(sw => sw.Workout)
                .Where(sw => sw.SessionId == sessionId)
                .ToListAsync();

            // Separate warmups from main workouts
            var warmups = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() == "WARMUP")
                .OrderBy(w => w.OrderNo)
                .ToList();

            var mainWorkouts = allWorkouts
                .Where(w => w.Workout != null && w.Workout.Category?.ToUpper() != "WARMUP")
                .OrderBy(w => w.OrderNo)
                .ToList();

            int order = 1;

            // First assign orders to warmups
            foreach (var warmup in warmups)
            {
                warmup.OrderNo = order++;
            }

            // Then assign orders to main workouts
            foreach (var workout in mainWorkouts)
            {
                workout.OrderNo = order++;
            }
            
            _logger.LogInformation($"Reordered {allWorkouts.Count} workouts for session {sessionId}");
        }
}   