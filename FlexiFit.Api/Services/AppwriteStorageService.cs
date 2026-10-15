using Appwrite;
using Appwrite.Services;
using Appwrite.Models;
using FlexiFit.Api.Helpers;

namespace FlexiFit.Api.Services;

public class AppwriteStorageService : IBlobService
{
    private readonly Storage _storage;
    private readonly string _endpoint;
    private readonly string _projectId;
    private readonly ILogger<AppwriteStorageService> _logger;

    public AppwriteStorageService(IConfiguration config, ILogger<AppwriteStorageService> logger)
    {
        _endpoint = config["StorageSettings:Appwrite:Endpoint"] ?? "https://sgp.cloud.appwrite.io/v1";
        _projectId = config["StorageSettings:Appwrite:ProjectId"] ?? "";
        var apiKey = config["StorageSettings:Appwrite:ApiKey"] ?? "";

        _logger = logger;

        var client = new Client()
            .SetEndpoint(_endpoint)
            .SetProject(_projectId)
            .SetKey(apiKey);

        _storage = new Storage(client);
    }

    public async Task<string> UploadFileAsync(IFormFile file, string containerName)
    {
        try
        {
            var fileName = file.FileName; // mimehelper for appwrite 8.0.0
            var mimeType = MimeTypeHelper.GetMimeType(fileName); 

            using var fileStream = file.OpenReadStream();
            var inputFile = InputFile.FromStream(fileStream, fileName, mimeType);

            var uploadedFile = await _storage.CreateFile(
                bucketId: containerName,
                fileId: ID.Unique(),
                file: inputFile
            );

            _logger.LogInformation("File uploaded successfully to {Container}.", containerName);

            return GetFileUrl(uploadedFile.Id, containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"Appwrite upload failed for container {Container}.", containerName);
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string fileName, string containerName)
    {
        try
        {
            await _storage.DeleteFile(containerName, fileName);
            _logger.LogInformation("File deleted successfully from {Container}.", containerName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Appwrite delete failed for container {Container}, file: {FileName}.", containerName, fileName);
            return false;
        }
    }

    public string GetFileUrl(string fileName, string containerName)
    {
        // Appwrite file view URL
        return $"{_endpoint}/storage/buckets/{containerName}/files/{fileName}/view?project={_projectId}";
    }
}