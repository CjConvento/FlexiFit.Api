namespace FlexiFit.Api.Services;

/// <summary>
/// Centralized constants for program cycle calculations.
/// Single source of truth to avoid hardcoded values scattered across services.
/// </summary>
public static class ProgramConstants
{
    /// <summary>Standard duration of one program cycle in days.</summary>
    public const int DaysPerCycle = 28;
    
    /// <summary>Number of days in a week.</summary>
    public const int DaysPerWeek = 7;
    
    /// <summary>Number of weeks in a standard cycle.</summary>
    public const int WeeksPerCycle = 4;
    
    /// <summary>
    /// Total days in a year for continuous tracking (Advanced/Rehab).
    /// Computed as: 12 months * 28 days = 336 days.
    /// </summary>
    public const int DaysPerYear = 336; 
}