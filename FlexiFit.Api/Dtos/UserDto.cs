namespace FlexiFit.Api.DTOs;

public class UserDto
{
    public int UserId { get; set; }
    public string FirebaseUid { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public string AuthProvider { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}