namespace FlexiFit.Api.Dtos
{
    public class UpdateGoogleEmailDto
    {
        public string NewEmail { get; set; } = null!;
        public string NewFirebaseUid { get; set; } = null!;
    }
}