// Services/GoogleCloudStorageService.cs
// Google Cloud Storage integration for Janani media, journal photos, and generated assets.

using Google;
using Google.Apis.Auth.OAuth2;
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
    private readonly UrlSigner? _urlSigner;
    private readonly string _bucketName;
    private readonly ILogger<GoogleCloudStorageService> _logger;

    public GoogleCloudStorageService(ILogger<GoogleCloudStorageService> logger)
    {
        _logger = logger;
        _bucketName = Environment.GetEnvironmentVariable("GCP_STORAGE_BUCKET") ?? "janani-assets";

        try
        {
            // Initializes using Application Default Credentials (gcloud auth application-default login or Service Account)
            var credential = GoogleCredential.GetApplicationDefault();
            _storageClient = StorageClient.Create(credential);
            // The bucket has no public-read ACL (journal photos are personal
            // content), so reads always go through GetSignedUrlAsync rather
            // than a permanent public link. On Cloud Run this credential is a
            // ComputeCredential, so UrlSigner signs via the IAM Credentials
            // API — the runtime service account needs
            // roles/iam.serviceAccountTokenCreator on itself for that to work
            // (see deploy-cloudrun.sh).
            _urlSigner = UrlSigner.FromCredential(credential);
            _logger.LogInformation("Google Cloud Storage client successfully initialized for bucket: {Bucket}", _bucketName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Google Cloud Storage client unavailable ({Message}). Falling back to local storage.", ex.Message);
            _storageClient = null;
            _urlSigner = null;
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

    // Bucket/objects are private -- a permanent "public" MediaLink would just
    // 403. Callers store only the object name and resolve a fresh signed URL
    // each time a photo is displayed. Returns null if signing is unavailable
    // (no storage client) or the signing request itself fails.
    public async Task<string?> GetSignedUrlAsync(string objectName, CancellationToken ct = default)
    {
        if (_urlSigner == null) return null;
        try
        {
            return await _urlSigner.SignAsync(_bucketName, objectName, TimeSpan.FromMinutes(15), HttpMethod.Get, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to sign GCS URL for {ObjectName}: {Message}", objectName, ex.Message);
            return null;
        }
    }

    private static string fileNamePrefixes(string prefix) => prefix.ToLowerInvariant().Replace(" ", "-");
}
