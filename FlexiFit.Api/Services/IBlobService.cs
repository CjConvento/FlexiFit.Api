namespace FlexiFit.Api.Services;

public interface IBlobService
{
    Task<string> UploadFileAsync(IFormFile file,string containerName);
    Task<bool> DeleteFileAsync(string fileName, string containerName);
    string GetFileUrl(string fileName, string containerName);
}