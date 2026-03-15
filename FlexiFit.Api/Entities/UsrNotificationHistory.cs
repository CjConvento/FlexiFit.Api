using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Entities;

public partial class UsrNotificationHistory
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string Type { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
