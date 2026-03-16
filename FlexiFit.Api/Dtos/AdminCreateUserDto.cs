namespace FlexiFit.Api.DTOs
{
    public class UserCreateDto
    {
        public string? firebase_uid { get; set; }
        public string name { get; set; } = string.Empty;
        public string username { get; set; } = string.Empty;
        public string email { get; set; } = string.Empty;
        public string role { get; set; } = "USER"; // Default to User
        public string auth_provider { get; set; } = "EMAIL"; // manual o google
    }
}