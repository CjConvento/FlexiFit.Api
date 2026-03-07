using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class WrkProgramTemplateDay
{
    public int TemplateDayId { get; set; }

    public int ProgramId { get; set; }

    public int MonthNo { get; set; }

    public int WeekNo { get; set; }

    public int DayNo { get; set; }

    public string DayType { get; set; } = null!;

    public string? Notes { get; set; }

    public virtual WrkProgramTemplate Program { get; set; } = null!;
}
