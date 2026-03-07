using System;
using System.Collections.Generic;

namespace FlexiFit.Api.Models;

public partial class UsrUserProfile
{
    public int ProfileId { get; set; }

    public int UserId { get; set; }

    public string? Name { get; set; }

    public string? Username { get; set; }

    public DateOnly? BirthDate { get; set; }

    public string? Gender { get; set; }

    public string? AvatarUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UsrUser User { get; set; } = null!;
}
