namespace FlexiFit.Api.Services;

/// <summary>
/// Immutable result ng macro calculation.
/// NOTE: Calories dito ay *intake target* (food), HINDI net target.
/// </summary>
public record MacroTargets(int Calories, int ProteinG, int CarbsG, int FatsG);

/// <summary>
/// Single source of truth para sa BMR, TDEE, goal adjustment, dietary split,
/// at workout-burn adjustment. Gagamitin ng onboarding, weight update,
/// update-full, at daily meal seeding — isang formula lang lahat.
/// </summary>
public class NutritionCalculator
{
    // ── Dietary split (% of calories), by dietary type ────────────────
    private static readonly Dictionary<string, (double Carbs, double Protein, double Fats)> DietarySplits
        = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BALANCED",     (0.45, 0.30, 0.25) },
        { "KETO",         (0.05, 0.25, 0.70) },
        { "HIGH_PROTEIN", (0.35, 0.40, 0.25) },
        { "VEGAN",        (0.50, 0.20, 0.30) },
        { "VEGETARIAN",   (0.50, 0.20, 0.30) },
        { "LACTOSE_FREE", (0.45, 0.30, 0.25) },
    };

    // ── Protein floor (g per kg body weight), by goal ────────────────
    private static readonly Dictionary<string, double> ProteinPerKgByGoal
        = new(StringComparer.OrdinalIgnoreCase)
    {
        { "MAINTAIN", 1.5 },
        { "LOSE",     1.6 },
        { "GAIN",     1.8 },
    };

    public MacroTargets Calculate(
        double weightKg,
        double heightCm,
        int age,
        string? gender,
        string? activityLevel,
        string? goal,
        string? dietaryType,
        double workoutCaloriesBurned = 0)
    {
        // Normalize all string inputs (multi-client safe)
        bool isMale    = string.Equals(gender?.Trim(), "MALE", StringComparison.OrdinalIgnoreCase);
        string actKey  = NormalizeActivity(activityLevel);
        string goalKey = NormalizeGoal(goal);
        string dietKey = NormalizeDiet(dietaryType);
        bool isKeto    = dietKey == "KETO";

        // 1. BMR (Mifflin-St Jeor)
        double bmr = isMale
            ? (10 * weightKg) + (6.25 * heightCm) - (5 * age) + 5
            : (10 * weightKg) + (6.25 * heightCm) - (5 * age) - 161;

        // 2. Activity multiplier (normalized)
        double multiplier = actKey switch
        {
            "SEDENTARY"                    => 1.2,
            "LIGHTLYACTIVE"                => 1.375,
            "ACTIVE" or "MODERATELYACTIVE" => 1.55,
            "VERYACTIVE"                   => 1.725,
            _                              => 1.2
        };

        double tdee = bmr * multiplier;

        // 3. Goal adjustment
        double calorieTarget = tdee;
        if (goalKey == "LOSE")      calorieTarget -= 500;
        else if (goalKey == "GAIN") calorieTarget += 300;

        // 4. Workout burn (Item 3 hook)
        calorieTarget += Math.Max(0, workoutCaloriesBurned);
        calorieTarget = Math.Max(calorieTarget, 1200); // safety floor

        // 5. Dietary split (primary)
        if (!DietarySplits.TryGetValue(dietKey, out var split))
            split = DietarySplits["BALANCED"];

        double proteinG = (calorieTarget * split.Protein) / 4.0;
        double carbsG   = (calorieTarget * split.Carbs)   / 4.0;
        double fatsG    = (calorieTarget * split.Fats)    / 9.0;

        // 6. Protein floor (g/kg) — skip KETO (intentional moderate protein)
        if (!isKeto)
        {
            double floorG = weightKg * ProteinPerKgByGoal[goalKey];

            if (proteinG < floorG)
            {
                double deltaCal = (floorG - proteinG) * 4.0;
                proteinG = floorG;
                carbsG   = Math.Max(50, carbsG - (deltaCal / 4.0));
            }
        }

        // 7. Carbs floor — skip KETO
        if (!isKeto) carbsG = Math.Max(carbsG, 50);

        return new MacroTargets(
            Calories: (int)Math.Round(calorieTarget),
            ProteinG: (int)Math.Round(proteinG),
            CarbsG:   (int)Math.Round(carbsG),
            FatsG:    (int)Math.Round(fatsG)
        );
    }

        // ═══════════════════════════════════════════════════════════════
    // NORMALIZATION HELPERS (multi-client safe)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Normalize goal strings from any client.
    /// "LOSE", "Lose Weight", "WEIGHT LOSS", "FAT_LOSS" → "LOSE"
    /// "GAIN", "Gain Muscle", "MUSCLE_GAIN", "BULK" → "GAIN"
    /// else → "MAINTAIN"
    /// </summary>
    public static string NormalizeGoal(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal)) return "MAINTAIN";

        string g = goal.Trim().ToUpperInvariant()
                       .Replace(" ", "").Replace("_", "").Replace("-", "");

        if (g.Contains("LOSE") || g.Contains("LOSS") || g.Contains("CUT"))
            return "LOSE";

        if (g.Contains("GAIN") || g.Contains("BULK") || g.Contains("MUSCLE"))
            return "GAIN";

        return "MAINTAIN";
    }

    /// <summary>
    /// Normalize dietary type strings.
    /// "High Protein", "HIGH_PROTEIN", "HIGH-PROTEIN" → "HIGH_PROTEIN"
    /// "Lactose Free", "LACTOSE_FREE" → "LACTOSE_FREE"
    /// else → "BALANCED"
    /// </summary>
    public static string NormalizeDiet(string? dietaryType)
    {
        if (string.IsNullOrWhiteSpace(dietaryType)) return "BALANCED";

        string d = dietaryType.Trim().ToUpperInvariant()
                              .Replace(" ", "").Replace("_", "").Replace("-", "");

        return d switch
        {
            "KETO" or "KETOGENIC"              => "KETO",
            "HIGHPROTEIN" or "PROTEIN"         => "HIGH_PROTEIN",
            "VEGAN"                            => "VEGAN",
            "VEGETARIAN" or "VEGGIE"           => "VEGETARIAN",
            "LACTOSEFREE" or "DAIRYFREE"       => "LACTOSE_FREE",
            "BALANCED" or "NORMAL" or "STD"    => "BALANCED",
            _                                  => "BALANCED"
        };
    }

    /// <summary>
    /// Normalize activity level strings.
    /// "Moderately Active", "MODERATELY_ACTIVE", "ACTIVE" → "ACTIVE"
    /// else → "SEDENTARY" (safe default)
    /// </summary>
    public static string NormalizeActivity(string? activityLevel)
    {
        if (string.IsNullOrWhiteSpace(activityLevel)) return "SEDENTARY";

        string a = activityLevel.Trim().ToUpperInvariant()
                                .Replace(" ", "").Replace("_", "").Replace("-", "");

        return a switch
        {
            "SEDENTARY" or "NONE" or "LOW"               => "SEDENTARY",
            "LIGHTLYACTIVE" or "LIGHT" or "LIGHTACTIVE"  => "LIGHTLYACTIVE",
            "ACTIVE" or "MODERATELYACTIVE" or "MODERATE" => "ACTIVE",
            "VERYACTIVE" or "HIGHLYACTIVE" or "INTENSE"  => "VERYACTIVE",
            _                                             => "SEDENTARY"
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // GOAL MET CHECK (Item 4: Performance indicator, NOT a blocker)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if the user's net calories are within 10% of their target.
    /// This is a PERFORMANCE INDICATOR only — hindi dapat gamitin bilang
    /// blocker sa day completion o progression advance.
    /// </summary>
    public static bool CheckGoalMet(double netCalories, double targetCalories)
    {
        if (targetCalories <= 0) return false;
        return Math.Abs(netCalories - targetCalories) <= targetCalories * 0.10;
    }

    /// <summary>
    /// Calculates the daily intake target (food calories needed)
    /// based on net target + workout burn.
    /// </summary>
    public static double CalculateIntakeTarget(double targetNetCalories, double workoutCaloriesBurned)
    {
        double intake = targetNetCalories + Math.Max(0, workoutCaloriesBurned);
        return Math.Max(intake, 500); // Safety floor
    }
}