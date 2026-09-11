namespace FlexiFit.Api.Helpers;

public static class MimeTypeHelper
{
    // ✅ Common image MIME types
    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        { ".jpg",  "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png",  "image/png" },
        { ".gif",  "image/gif" },
        { ".webp", "image/webp" },
        { ".bmp",  "image/bmp" },
        { ".svg",  "image/svg+xml" },
        { ".ico",  "image/x-icon" },
        { ".tiff", "image/tiff" },
        { ".tif",  "image/tiff" },

        // Documents
        { ".pdf",  "application/pdf" },
        { ".doc",  "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls",  "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },

        // Text
        { ".txt",  "text/plain" },
        { ".csv",  "text/csv" },
        { ".json", "application/json" },
        { ".xml",  "application/xml" },

        // Video
        { ".mp4",  "video/mp4" },
        { ".webm", "video/webm" },
        { ".mov",  "video/quicktime" },

        // Audio
        { ".mp3",  "audio/mpeg" },
        { ".wav",  "audio/wav" },
        { ".ogg",  "audio/ogg" }
    };

    /// <summary>
    /// Determines the MIME type from a file extension.
    /// Returns "application/octet-stream" if the extension is not recognized.
    /// </summary>
    public static string GetMimeType(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "application/octet-stream";

        var extension = Path.GetExtension(fileName);

        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        return _mimeTypes.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }
}