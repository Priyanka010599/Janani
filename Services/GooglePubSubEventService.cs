// Services/GooglePubSubEventService.cs
// Google Cloud Pub/Sub event bus service for Janani multi-agent event orchestration.

using System.Text.Json;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.PubSub.V1;

namespace Janani.Services;

public class JananiAgentEvent
{
    public required string EventId { get; set; } = Guid.NewGuid().ToString();
    public required string EventType { get; set; }
    public required string SourceAgent { get; set; }
    public required string UserName { get; set; }
    public int PregnancyWeek { get; set; }
    public object? Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class GooglePubSubEventService
{
    private readonly string _projectId;
    private readonly ILogger<GooglePubSubEventService> _logger;
    private readonly Dictionary<string, PublisherClient> _publisherCache = [];

    public GooglePubSubEventService(ILogger<GooglePubSubEventService> logger)
    {
        _logger = logger;
        _projectId = Environment.GetEnvironmentVariable("GCP_PROJECT_ID") ?? "janani-project";
    }

    public async Task<bool> CheckPubSubHealthAsync(CancellationToken ct = default)
    {
        try
        {
            var publisherService = await PublisherServiceApiClient.CreateAsync(ct);
            var projectName = ProjectName.FromProject(_projectId);
            var topics = publisherService.ListTopics(projectName);
            return topics.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Pub/Sub health check note ({Message}). Running in local memory event mode.", ex.Message);
            return false;
        }
    }

    public async Task<bool> PublishEventAsync(
        string topicId, JananiAgentEvent agentEvent, CancellationToken ct = default)
    {
        var topicName = TopicName.FromProjectTopic(_projectId, topicId);
        
        try
        {
            if (!_publisherCache.TryGetValue(topicId, out var publisher))
            {
                publisher = await PublisherClient.CreateAsync(topicName);
                _publisherCache[topicId] = publisher;
            }

            var jsonPayload = JsonSerializer.Serialize(agentEvent);
            var pubsubMessage = new PubsubMessage
            {
                Data = Google.Protobuf.ByteString.CopyFromUtf8(jsonPayload),
                Attributes =
                {
                    { "eventType", agentEvent.EventType },
                    { "sourceAgent", agentEvent.SourceAgent },
                    { "user", agentEvent.UserName }
                }
            };

            var messageId = await publisher.PublishAsync(pubsubMessage);
            _logger.LogInformation("Published event {EventType} to Pub/Sub topic {Topic}: {MessageId}",
                agentEvent.EventType, topicId, messageId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Handled Pub/Sub event locally ({EventType}): {Message}",
                agentEvent.EventType, ex.Message);
            return false;
        }
    }
}
