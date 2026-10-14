using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace FlexiFit.Api.Services;

public class BlobService : IBlobService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _baseUrl;
    private readonly ILogger<BlobService> _logger;

    // Business Rules: Centralized at madaling baguhin
    private const long MaxFileSizeInBytes = 5 * 1024 * 1024; // 5MB
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public BlobService(
        IConfiguration configuration,
        ILogger<BlobService> logger)
    {
        var connectionString = configuration["AzureStorage:ConnectionString"];
        _blobServiceClient = new BlobServiceClient(connectionString);
        _baseUrl = configuration["AzureStorage:BaseUrl"] ?? "https://flexifitstorage.blob.core.windows.net";
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string containerName)
    {
        // 1. Validation (Business Rules)
        if (file == null || file.Length == 0)
            throw new ArgumentException("No file uploaded.");

        if (file.Length > MaxFileSizeInBytes)
            throw new InvalidOperationException($"File size exceeds the {MaxFileSizeInBytes / (1024 * 1024)}MB limit.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Unsupported file type: {ext}. Allowed: {string.Join(", ", AllowedExtensions)}");

        // 2. Unique Filename Generation
        var uniqueFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";

        // 3. Azure Upload Logic
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobClient = containerClient.GetBlobClient(uniqueFileName);
        var contentType = GetContentType(ext);
        var headers = new BlobHttpHeaders { ContentType = contentType };

        using var stream = file.OpenReadStream();
        await blobClient.UploadAsync(stream, headers);

        _logger.LogInformation("File '{FileName}' uploaded successfully to container '{Container}'.", uniqueFileName, containerName);

        // 4. Return the clean URL
        return GetFileUrl(uniqueFileName, containerName);
    }

    public async Task<bool> DeleteFileAsync(string fileName, string containerName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(fileName);
        
        var deleted = await blobClient.DeleteIfExistsAsync();
        if (deleted)
        {
            _logger.LogInformation("File '{FileName}' deleted successfully from container '{Container}'.", fileName, containerName);
        }
        return deleted;
    }

    public string GetFileUrl(string fileName, string containerName)
    {
        return $"{_baseUrl}/{containerName}/{fileName}";
    }

    private string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
    }
}