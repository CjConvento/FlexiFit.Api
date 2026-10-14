using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Services;
using System.ComponentModel.DataAnnotations;

namespace FlexiFit.Api.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("api/blob")]
[ApiController]
public class BlobController : ControllerBase
{
    private readonly IBlobService _blobService;

    public BlobController(IBlobService blobService)
    {
        _blobService = blobService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file, 
        [FromQuery] string container)
    {
        // Pure Orchestration: 
        // Kung invalid ang file (null, sobrang laki, maling type), magtthrow ang Service.
        // Automatic na hahawakan ng Global Exception Middleware at magre-return ng 400 Bad Request!
        var fileUrl = await _blobService.UploadFileAsync(file, container);
        
        return Ok(new { url = fileUrl });
    }

    [HttpDelete("{container}/{fileName}")]
    public async Task<IActionResult> Delete(
        [FromRoute][Required] string container, 
        [FromRoute][Required] string fileName)
    {
        var deleted = await _blobService.DeleteFileAsync(fileName, container);
        
        if (!deleted)
            return NotFound(new { message = "File not found in blob storage." });

        return NoContent();
    }
}