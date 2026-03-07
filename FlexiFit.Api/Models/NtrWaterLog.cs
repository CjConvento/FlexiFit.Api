using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class NtrWaterLog
{
    public int WaterLogId { get; set; }

    public int UserId { get; set; }

    public int WaterMl { get; set; }

    public DateOnly LogDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
