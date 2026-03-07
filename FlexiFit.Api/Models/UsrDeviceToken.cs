using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class UsrDeviceToken
{
    public int DeviceTokenId { get; set; }

    public int UserId { get; set; }

    public string FcmToken { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
