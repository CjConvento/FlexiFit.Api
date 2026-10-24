using FlexiFit.Api.Dtos;
using FlexiFit.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlexiFit.Api.Services;

public class CalendarService
{
    private readonly FlexiFitDbContext _context;

    public CalendarService(FlexiFitDbContext context)
    {
        _context = context;
    }

    // ==========================================
    // CALENDAR FETCHING LOGIC
    // ==========================================
    public async Task<List<CalendarHistoryDto>> GetCalendarHistoryAsync(int userId)
    {
        // 1. Get Active Program
        var activeProgram = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
            
        if (activeProgram == null)
            throw new KeyNotFoundException("No active program found.");

        // 2. Get Template Days Pattern (Week 1 - Days 1-7)
        var templateDays = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId && d.WeekNo == 1)
            .OrderBy(d => d.DayNo)
            .Select(d => new { d.DayNo, d.DayType, d.Notes })
            .ToListAsync();

        var templateMap = templateDays.ToDictionary(d => d.DayNo, d => d.DayType);

        // 3. Get User Workout Sessions
        var workoutSessions = await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId)
            .ToListAsync();

        var workoutMap = workoutSessions
            .GroupBy(s => s.WorkoutDay)
            .Select(g => g.OrderByDescending(s => s.CreatedAt).First())
            .ToDictionary(s => s.WorkoutDay, s => s);

        // 4. Get Nutrition Calendar Entries
        var nutritionDays = await _context.NtrMealPlanCalendars
            .Where(c => c.CycleId == activeProgram.CycleNo)
            .ToListAsync();

        var nutritionMap = nutritionDays
            .GroupBy(c => c.DayNo)
            .Select(g => g.OrderByDescending(c => c.UpdatedAt).First())
            .ToDictionary(c => c.DayNo, c => c);

        var historyList = new List<CalendarHistoryDto>();

        // 5. Generate 28 Days (4 Weeks x 7 Days)
        for (int day = 1; day <= 28; day++)
        {
            int templateDayNo = ((day - 1) % 7) + 1;
            string dayType = templateMap.GetValueOrDefault(templateDayNo, "WORKOUT");
            int weekNo = ((day - 1) / 7) + 1;

            workoutMap.TryGetValue(day, out var workoutSession);
            nutritionMap.TryGetValue(day, out var nutritionDay);
            bool isRestDay = dayType.Contains("REST", StringComparison.OrdinalIgnoreCase);

            var nutritionStatus = GetNutritionStatus(nutritionDay, day, activeProgram.CurrentDayNo);

            historyList.Add(new CalendarHistoryDto
            {
                Day = day,
                Week = weekNo,
                DayType = dayType,
                Summary = GetDaySummary(dayType, weekNo),
                WorkoutStatus = GetWorkoutStatus(workoutSession, day, activeProgram.CurrentDayNo, isRestDay),
                NutritionStatus = nutritionStatus,
                Status = GetOverallStatus(workoutSession, nutritionStatus, day, activeProgram.CurrentDayNo, isRestDay),
                IsCurrentDay = day == activeProgram.CurrentDayNo,
                IsFuture = day > activeProgram.CurrentDayNo,
                IsPast = day < activeProgram.CurrentDayNo
            });
        }

        return historyList;
    }

    public async Task<object> GetWeekScheduleAsync(int userId, int weekNo)
    {
        var activeProgram = await GetActiveProgramOrThrowAsync(userId);

        if (weekNo < 1 || weekNo > 4)
            throw new ArgumentException("Week must be between 1 and 4");

        var templateDays = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId && d.WeekNo == 1)
            .OrderBy(d => d.DayNo)
            .Select(d => new { d.DayNo, d.DayType })
            .ToListAsync();

        var weekDays = new List<object>();
        int startDay = (weekNo - 1) * 7 + 1;

        for (int i = 0; i < 7; i++)
        {
            var template = i < templateDays.Count
                ? templateDays[i]
                : new { DayNo = i + 1, DayType = "WORKOUT" };

            weekDays.Add(new
            {
                day = startDay + i,
                templateDay = template.DayNo,
                dayType = template.DayType,
                week = weekNo
            });
        }

        return new { week = weekNo, startDay = startDay, endDay = startDay + 6, days = weekDays };
    }

    public async Task<object> GetDayDetailsAsync(int userId, int dayNo)
    {
        var activeProgram = await GetActiveProgramOrThrowAsync(userId);

        int templateDayNo = ((dayNo - 1) % 7) + 1;
        
        var templateDay = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId && d.DayNo == templateDayNo)
            .Select(d => new { d.DayType, d.Notes })
            .FirstOrDefaultAsync();

        var workoutSession = await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId && s.WorkoutDay == dayNo)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        var nutritionDay = await _context.NtrMealPlanCalendars
            .Where(c => c.CycleId == activeProgram.CycleNo && c.DayNo == dayNo)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        return new
        {
            day = dayNo,
            week = ((dayNo - 1) / 7) + 1,
            templateDay = templateDayNo,
            dayType = templateDay?.DayType ?? "WORKOUT",
            notes = templateDay?.Notes,
            workout = workoutSession != null ? new
            {
                sessionId = workoutSession.SessionId,
                status = workoutSession.Status,
                completedAt = workoutSession.CompletedAt,
                startedAt = workoutSession.StartedAt
            } : null,
            nutrition = nutritionDay != null ? new
            {
                status = nutritionDay.Status,
                updatedAt = nutritionDay.UpdatedAt
            } : null,
            isCurrentDay = dayNo == activeProgram.CurrentDayNo,
            isPastDay = dayNo < activeProgram.CurrentDayNo,
            isFutureDay = dayNo > activeProgram.CurrentDayNo
        };
    }

    public async Task<object> GetProgramProgressAsync(int userId)
    {
        var activeProgram = await GetActiveProgramOrThrowAsync(userId);

        var completedWorkouts = await _context.UsrUserWorkoutSessions
            .Where(s => s.ProgramInstanceId == activeProgram.InstanceId && s.Status == "COMPLETED")
            .Select(s => s.WorkoutDay)
            .Distinct()
            .CountAsync();

        var completedNutrition = await _context.NtrMealPlanCalendars
            .Where(c => c.CycleId == activeProgram.CycleNo && c.Status == "DONE")
            .Select(c => c.DayNo)
            .Distinct()
            .CountAsync();

        int currentWeek = ((activeProgram.CurrentDayNo - 1) / 7) + 1;
        int currentDayInWeek = ((activeProgram.CurrentDayNo - 1) % 7) + 1;

        return new
        {
            currentDay = activeProgram.CurrentDayNo,
            currentWeek = currentWeek,
            currentDayInWeek = currentDayInWeek,
            totalDays = 28,
            totalWeeks = 4,
            completedWorkouts = completedWorkouts,
            completedNutrition = completedNutrition,
            progress = Math.Round((double)(activeProgram.CurrentDayNo - 1) / 28 * 100, 1),
            programStatus = activeProgram.Status
        };
    }

    public async Task<object> GetFullScheduleAsync(int userId)
    {
        var activeProgram = await GetActiveProgramOrThrowAsync(userId);

        var templateDays = await _context.WrkProgramTemplateDays
            .Where(d => d.ProgramId == activeProgram.ProgramId && d.WeekNo == 1)
            .OrderBy(d => d.DayNo)
            .Take(7)
            .Select(d => new { d.DayNo, d.DayType })
            .ToListAsync();

        var fullSchedule = new List<object>();

        for (int week = 1; week <= 4; week++)
        {
            var weekDays = new List<object>();
            int startDay = (week - 1) * 7 + 1;

            for (int i = 0; i < 7; i++)
            {
                var template = i < templateDays.Count
                    ? templateDays[i]
                    : new { DayNo = i + 1, DayType = "WORKOUT" };

                weekDays.Add(new
                {
                    day = startDay + i,
                    dayType = template.DayType,
                    isRestDay = template.DayType.Contains("REST", StringComparison.OrdinalIgnoreCase)
                });
            }

            fullSchedule.Add(new { week = week, days = weekDays });
        }

        return new
        {
            programId = activeProgram.ProgramId,
            programName = activeProgram.Program?.ProgramName,
            schedule = fullSchedule,
            note = "Weeks 2, 3, and 4 repeat the pattern from Week 1"
        };
    }

    // ── Private Helpers ──────────────────────────────────────────
    private async Task<UsrUserProgramInstance> GetActiveProgramOrThrowAsync(int userId)
    {
        var program = await _context.UsrUserProgramInstances
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Status == "ACTIVE");
            
        if (program == null)
            throw new KeyNotFoundException("No active program found.");
            
        return program;
    }

    private string GetDaySummary(string dayType, int weekNo)
    {
        if (dayType.Contains("REST", StringComparison.OrdinalIgnoreCase))
            return "Rest Day";
        return $"{dayType} - Week {weekNo}";
    }

    private string GetWorkoutStatus(UsrUserWorkoutSession? session, int day, int currentDay, bool isRestDay)
    {
        if (session != null) return session.Status.ToUpper();
        if (isRestDay && day <= currentDay) return "COMPLETED";
        if (day < currentDay) return "SKIPPED";
        if (day == currentDay) return "PENDING";
        return "NOT_STARTED";
    }

    private string GetNutritionStatus(NtrMealPlanCalendar? nutritionDay, int day, int currentDay)
    {
        if (nutritionDay != null)
            return nutritionDay.Status.ToUpper() == "DONE" ? "COMPLETED" : nutritionDay.Status.ToUpper();
        if (day < currentDay) return "SKIPPED";
        if (day == currentDay) return "PENDING";
        return "NOT_STARTED";
    }

    private string GetOverallStatus(UsrUserWorkoutSession? session, string nutritionStatus, int day, int currentDay, bool isRestDay)
    {
        if (isRestDay) return day <= currentDay ? "COMPLETED" : "NOT_STARTED";
        
        bool workoutDone = session?.Status?.ToUpper() == "COMPLETED";
        bool nutritionDone = nutritionStatus == "COMPLETED";
        
        if (workoutDone && nutritionDone) return "COMPLETED";
        if (day < currentDay) return "SKIPPED";
        if (day == currentDay) return "PENDING";
        return "NOT_STARTED";
    }
}