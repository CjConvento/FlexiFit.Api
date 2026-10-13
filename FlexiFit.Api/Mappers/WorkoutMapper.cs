using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;

namespace FlexiFit.Api.Mappers;

public static class WorkoutMapper
{
    // ── Single Exercise Mapping ──────────────────────────────────────
    public static WorkoutExerciseDto ToWorkoutExerciseDto(UsrUserSessionWorkout sw, string baseUrl, bool isCompleted)
    {
        if (sw.Workout == null)
        {
            return new WorkoutExerciseDto
            {
                Id = sw.WorkoutId,
                Name = "Unknown Exercise",
                ImageFileName = $"{baseUrl}/images/workouts/default.png",
                MuscleGroup = "Unknown",
                Sets = sw.Sets,
                Reps = sw.Reps,
                DurationMinutes = 10,
                RestSeconds = 60,
                Calories = 50,
                Description = "Exercise details not available",
                VideoUrl = null,
                Order = sw.OrderNo,
                LoadKg = sw.LoadKg,
                IsCompleted = isCompleted
            };
        }

        string imageUrl = ResolveImageUrl(sw.Workout.ImgFilename, sw.Workout.Category, baseUrl);

        return new WorkoutExerciseDto
        {
            Id = sw.WorkoutId,
            Name = sw.Workout.WorkoutName ?? "Unknown Exercise",
            ImageFileName = imageUrl,
            MuscleGroup = sw.Workout.MuscleGroup ?? "Full Body",
            Sets = sw.Sets,
            Reps = sw.Reps,
            DurationMinutes = sw.Workout.Duration ?? 10,
            RestSeconds = 60,
            Calories = (int)(sw.Workout.CaloriesBurned ?? 50),
            Description = sw.Workout.Notes ?? "Perform with proper form",
            VideoUrl = sw.Workout.VideoUrl,
            Order = sw.OrderNo,
            LoadKg = sw.LoadKg,
            IsCompleted = isCompleted
        };
    }

    // ── Full DailyWorkoutPlanDto Builder
    public static DailyWorkoutPlanDto BuildDailyWorkoutPlanDto(
        int dayNo,
        string dayType,
        string status,
        string? level,
        string message,
        bool canSkip,
        string? skipMessage,
        int sessionId,
        WorkoutProgramDto program,
        List<WorkoutExerciseDto> warmups,
        List<WorkoutExerciseDto> workouts
    )
    {
        return new DailyWorkoutPlanDto
        {
            DayNo = dayNo,
            DayType = dayType,
            Status = status,
            Level = level,
            FocusArea = dayType,
            Message = message,
            CanSkip = canSkip,
            SkipMessage = skipMessage,
            SessionId = sessionId,
            Program = program,
            Warmups = warmups,
            Workouts = workouts,
            TotalCalories = warmups.Sum(x => x.Calories) + workouts.Sum(x => x.Calories),
            TotalDuration = warmups.Sum(x => x.DurationMinutes) + workouts.Sum(x => x.DurationMinutes)
        };
    }


    // ── Program DTO Builder ──────────────────────────────────────────
    public static WorkoutProgramDto BuildProgramDto(
        UsrUserProgramInstance p,
        int programNumber,
        int totalPrograms,
        int month,
        int week,
        int day
    )
    {
        return new WorkoutProgramDto
        {
            ProgramId = p.ProgramId,
            ProgramName = p.Program?.ProgramName ?? "My Program",
            Description = p.Program?.Description ?? "",
            Environment = p.Program?.Environment ?? "Any",
            Level = p.Program?.FitnessLevel ?? "Beginner",
            Status = p.Status,  
            Month = month,
            Week = week,
            Day = day,
            ProgramNumber = programNumber,   // for numbering
            TotalPrograms = totalPrograms       // Para sa arrow visibility           
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────
    public static string GetStatusMessage(string? status)
    {
        return status?.ToUpper() switch
        {
            "COMPLETED" => "🎉 Great job! You've completed today's workout!",
            "SKIPPED" => "This day was skipped. Ready for tomorrow? 💪",
            "PENDING" => "Ready to crush it today! Let's go! 💪",
            _ => "Let's get moving! 🏃"
        };
    }

    public static string GetCategoryFolder(string? category)
    {
        if (string.IsNullOrEmpty(category)) return "general";
        return category.ToLower() switch
        {
            "cardio" => "cardio", "strength" => "strength", "hiit" => "hiit",
            "yoga" => "yoga", "warmup" => "warmups", "push" => "push",
            "pull" => "pull", "legs" => "legs", "core" => "core",
            _ => "general"
        };
    }

    public static string ResolveImageUrl(string? fileName, string? category, string baseUrl)
    {
        if (string.IsNullOrEmpty(fileName)) return $"{baseUrl}/images/workouts/default.png";
        if (fileName.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return fileName;
        return $"{baseUrl}/images/workouts/default.png"; // Fallback for legacy
    }
}