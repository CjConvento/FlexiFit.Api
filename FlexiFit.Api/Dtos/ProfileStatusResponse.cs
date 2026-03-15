namespace FlexiFit.Api.Dtos
{
    public class ProfileStatusResponse
    {
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public string? FitnessLevel { get; set; }
        public string? Goal { get; set; }

        public BmiDataDto? BmiData { get; set; }
        public NutritionDataDto? Nutrition { get; set; }
        public ProgramDataDto? Program { get; set; }
    }

    public class BmiDataDto
    {
        public double Value { get; set; }
        public string? Status { get; set; }
    }

    public class NutritionDataDto
    {
        public double TargetCalories { get; set; }
        public double Intake { get; set; }
        public double Burned { get; set; }
        public double NetCalories { get; set; }
        public double Remaining { get; set; }
        public int WaterGlasses { get; set; }
        public int WaterTarget { get; set; }
    }

    public class ProgramDataDto
    {
        public string? Name { get; set; }
        public int DayNo { get; set; }
        public bool IsWorkoutDay { get; set; }
    }
}