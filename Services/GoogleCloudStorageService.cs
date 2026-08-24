// Services/GoogleCloudStorageService.cs
// Google Cloud Storage integration for Janani media, journal photos, and generated assets.

using Google;
using Google.Cloud.Storage.V1;

namespace Janani.Services;

public class StorageUploadResult
{
    public required string ObjectName { get; set; }
    public required string MediaLink { get; set; }
    public bool IsCloudStorage { get; set; }
}

public class GoogleCloudStorageService
{
    private readonly StorageClient? _storageClient;
    private readonly string _bucketName;
    private readonly ILogger<GoogleCloudStorageService> _logger;

    public GoogleCloudStorageService(ILogger<GoogleCloudStorageService> logger)
    {
        _logger = logger;
        _bucketName = Environment.GetEnvironmentVariable("GCP_STORAGE_BUCKET") ?? "janani-assets";

        try
        {
            // Initializes using Application Default Credentials (gcloud auth application-default login or Service Account)
            _storageClient = StorageClient.Create();
            _logger.LogInformation("Google Cloud Storage client successfully initialized for bucket: {Bucket}", _bucketName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Google Cloud Storage client unavailable ({Message}). Falling back to local storage.", ex.Message);
            _storageClient = null;
        }
    }

    public async Task<bool> CheckStorageHealthAsync(CancellationToken ct = default)
    {
        if (_storageClient == null) return false;
        try
        {
            var bucket = await _storageClient.GetBucketAsync(_bucketName, cancellationToken: ct);
            return bucket != null;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("GCS bucket {Bucket} not found, attempting auto-creation...", _bucketName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("GCS health check failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<StorageUploadResult> UploadImageAsync(
        byte[] imageBytes, string contentType, string fileNamePrefix = "journal-photo", CancellationToken ct = default)
    {
        var objectName = $"{fileNamePrefixes(fileNamePrefix)}/{Guid.NewGuid()}_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";

        if (_storageClient != null)
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                var obj = await _storageClient.UploadObjectAsync(
                    _bucketName,
                    objectName,
                    contentType,
                    stream,
                    cancellationToken: ct);

                var publicUrl = obj.MediaLink ?? $"https://storage.googleapis.com/{_bucketName}/{objectName}";
                _logger.LogInformation("Uploaded asset to GCS: {ObjectName}", objectName);

                return new StorageUploadResult
                {
                    ObjectName = objectName,
                    MediaLink = publicUrl,
                    IsCloudStorage = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError("GCS Upload failed ({Message}). Falling back to base64 encoding.", ex.Message);
            }
        }

        // Local fallback
        var base64Data = $"data:{contentType};base64,{Convert.ToBase64String(imageBytes)}";
        return new StorageUploadResult
        {
            ObjectName = objectName,
            MediaLink = base64Data,
            IsCloudStorage = false
        };
    }

    private static string fileNamePrefixes(string prefix) => prefix.ToLowerInvariant().Replace(" ", "-");
}
