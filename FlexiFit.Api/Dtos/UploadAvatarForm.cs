using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace FlexiFit.Api.Dtos
{
    public class UploadAvatarForm
    {
        [Required]
        public IFormFile File { get; set; } = default!;

        // Tinanggal natin yung UserId dito para sa security. 
        // Ang Backend Controller na ang bahalang kumuha ng ID sa Token.
    }
}