using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class ActActivitySummary
{
    public int SummaryId { get; set; }

    public int UserId { get; set; }

    public int CaloriesBurned { get; set; }

    public int TotalMinutes { get; set; }

    public DateOnly LogDate { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
