using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FlexiFit.Api.Services;

namespace FlexiFit.Api.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("api/blob")]
[ApiController]
public class BlobController : ControllerBase
{
    private readonly IBlobService _blobService;
    private readonly ILogger<BlobController> _logger;

    public BlobController(IBlobService blobService, ILogger<BlobController> logger)
    {
        _blobService = blobService;
        _logger = logger;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] string container)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });

        if (file.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "File size exceeds 5MB limit." });

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";

        try
        {
            using var stream = file.OpenReadStream();
            var blobUrl = await _blobService.UploadFileAsync(stream, uniqueFileName, container);

            _logger.LogInformation("File uploaded successfully to {Container}: {FileName}", container, uniqueFileName);

            return Ok(new 
            { 
                url = blobUrl, 
                fileName = uniqueFileName,
                container = container
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to {Container}", container);
            return StatusCode(500, new { message = "Upload failed. Please try again." });
        }
    }

    [HttpDelete("{container}/{fileName}")]
    public async Task<IActionResult> Delete(string container, string fileName)
    {
        try
        {
            var deleted = await _blobService.DeleteFileAsync(fileName, container);
            if (!deleted)
                return NotFound(new { message = "File not found in blob storage." });

            _logger.LogInformation("File deleted from {Container}: {FileName}", container, fileName);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from {Container}", container);
            return StatusCode(500, new { message = "Delete failed. Please try again." });
        }
    }
}