namespace FlexiFit.Api.Dtos
{
    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string? Role { get; set; }
        public string? Status { get; set; }
        public bool IsVerified { get; set; }
    }
}