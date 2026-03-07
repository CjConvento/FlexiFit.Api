using Microsoft.AspNetCore.Http;

namespace FlexiFit.Api.Dtos
{
    public class UploadAvatarForm
    {
        public IFormFile File { get; set; } = default!;
        public int UserId { get; set; }
    }
}