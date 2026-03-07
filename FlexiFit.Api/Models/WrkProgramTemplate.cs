using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class WrkProgramTemplate
{
    public int ProgramId { get; set; }

    public string ProgramName { get; set; } = null!;

    public string ProgramCategory { get; set; } = null!;

    public string FitnessLevel { get; set; } = null!;

    public int MonthsPerCycle { get; set; }

    public int WeeksPerMonth { get; set; }

    public int DaysPerWeek { get; set; }

    public string Environment { get; set; } = null!;

    public string Equipment { get; set; } = null!;

    public string SessionStructure { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<WrkProgramTemplateDay> WrkProgramTemplateDays { get; set; } = new List<WrkProgramTemplateDay>();

    public virtual ICollection<WrkProgramTemplateDaytypeWorkout> WrkProgramTemplateDaytypeWorkouts { get; set; } = new List<WrkProgramTemplateDaytypeWorkout>();
}
