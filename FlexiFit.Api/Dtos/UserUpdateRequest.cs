namespace FlexiFit.Api.DTOs;

public class UserUpdateRequest
{
    public int user_id { get; set; }
    public string? name { get; set; }
    public string username { get; set; } = string.Empty;
    public string email { get; set; } = string.Empty;
    public string role { get; set; } = string.Empty;
}