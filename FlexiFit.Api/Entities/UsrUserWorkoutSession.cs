using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrUserWorkoutSession
{
    public int SessionId { get; set; }

    public int UserId { get; set; }

    public int ProgramInstanceId { get; set; }

    public int WorkoutDay { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
