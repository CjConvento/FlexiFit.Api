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
        _endpoint = config["Appwrite:Endpoint"] ?? "https://sgp.cloud.appwrite.io/v1";
        _projectId = config["Appwrite:ProjectId"] ?? "";
        var apiKey = config["Appwrite:ApiKey"] ?? "";

        _logger = logger;

        var client = new Client()
            .SetEndpoint(_endpoint)
            .SetProject(_projectId)
            .SetKey(apiKey);

        _storage = new Storage(client);
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string containerName)
    {
        try
        {
            var mimeType = MimeTypeHelper.GetMimeType(fileName); // mimehelper for appwrite 8.0.0
            var inputFile = InputFile.FromStream(fileStream, fileName, mimeType);

            var file = await _storage.CreateFile(
                bucketId: containerName,
                fileId: ID.Unique(),
                file: inputFile
            );

            _logger.LogInformation("File uploaded successfully to {Container}.", containerName);

            return file.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError("Appwrite upload failed for container {Container}.", containerName);

            _logger.LogDebug(ex, "Appwrite upload error details");
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
            _logger.LogError("Appwrite delete failed for container {Container}.", containerName);
            _logger.LogDebug(ex, "Appwrite delete error details");
            return false;
        }
    }

    public string GetFileUrl(string fileName, string containerName)
    {
        // Appwrite file view URL
        return $"{_endpoint}/storage/buckets/{containerName}/files/{fileName}/view?project={_projectId}";
    }
}