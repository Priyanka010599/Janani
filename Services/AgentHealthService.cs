// Services/AgentHealthService.cs
// System health, agent readiness, GCS storage, and Pub/Sub monitoring for Janani multi-agent deployment.

using Janani.Data;
using Microsoft.EntityFrameworkCore;

namespace Janani.Services;

public class SystemHealthStatus
{
    public required string Status { get; set; }
    public required string AiProvider { get; set; }
    public bool DatabaseConnected { get; set; }
    public bool StorageConnected { get; set; }
    public bool PubSubConnected { get; set; }
    public bool CalendarSyncConfigured { get; set; }
    public int ActiveAgentCount { get; set; }
    public List<AgentHealthInfo> Agents { get; set; } = [];
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class AgentHealthInfo
{
    public required string Name { get; set; }
    public required string Role { get; set; }
    public bool Ready { get; set; } = true;
}

public class AgentHealthService(
    BloomAgentRegistry registry,
    AppDbContext dbContext,
    GoogleCloudStorageService storageService,
    GooglePubSubEventService pubSubService,
    ICalendarSyncService calendarSyncService)
{
    public async Task<SystemHealthStatus> CheckHealthAsync(CancellationToken ct = default)
    {
        var provider = Environment.GetEnvironmentVariable("AI_PROVIDER") ?? "vertex";
        
        bool dbOk = false;
        try
        {
            dbOk = await dbContext.Database.CanConnectAsync(ct);
        }
        catch
        {
            dbOk = false;
        }

        bool storageOk = await storageService.CheckStorageHealthAsync(ct);
        bool pubSubOk = await pubSubService.CheckPubSubHealthAsync(ct);
        bool calendarSyncOk = await calendarSyncService.CheckHealthAsync(ct);

        var agentList = registry.GetAllAgents();
        var healthInfos = agentList.Select(a => new AgentHealthInfo
        {
            Name = a.AgentName,
            Role = a.AgentRole,
            Ready = true
        }).ToList();

        var isHealthy = dbOk && healthInfos.Count > 0;

        return new SystemHealthStatus
        {
            Status = isHealthy ? "Healthy" : "Degraded",
            AiProvider = provider,
            DatabaseConnected = dbOk,
            StorageConnected = storageOk,
            PubSubConnected = pubSubOk,
            CalendarSyncConfigured = calendarSyncOk,
            ActiveAgentCount = healthInfos.Count,
            Agents = healthInfos,
            CheckedAt = DateTime.UtcNow
        };
    }
}
