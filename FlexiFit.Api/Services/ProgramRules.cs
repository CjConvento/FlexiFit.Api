using FlexiFit.Api.Entities;

namespace FlexiFit.Api.Services;

/// <summary>
/// Centralized program rules (single source of truth).
/// </summary>
public static class ProgramRules
{
    public const string RehabLevel = "REHAB LEVEL";

    public static bool IsRehabTemplate(WrkProgramTemplate? template) =>
        template != null &&
        ((template.FitnessLevel ?? "").Trim().Equals(RehabLevel, StringComparison.OrdinalIgnoreCase)
            || (template.ProgramName ?? "").Contains("Rehab", StringComparison.OrdinalIgnoreCase));
}       