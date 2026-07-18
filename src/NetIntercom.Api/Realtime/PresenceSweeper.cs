using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;
using NetIntercom.Infrastructure.Persistence;

namespace NetIntercom.Api.Realtime;

/// <summary>
/// Periodically reconciles in-memory presence: applies grace periods, broadcasts
/// transitions (online → away → offline) and persists PresenceSnapshot rows.
/// </summary>
public class PresenceSweeper : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly PresenceTracker _tracker;
    private readonly IHubContext<AgentHub> _agentHub;
    private readonly IHubContext<VisitorHub> _visitorHub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PresenceSweeper> _logger;

    private readonly Dictionary<Guid, PresenceStatus> _lastBroadcast = new();
    private bool _lastAnyOnline;

    public PresenceSweeper(
        PresenceTracker tracker,
        IHubContext<AgentHub> agentHub,
        IHubContext<VisitorHub> visitorHub,
        IServiceScopeFactory scopeFactory,
        ILogger<PresenceSweeper> logger)
    {
        _tracker = tracker;
        _agentHub = agentHub;
        _visitorHub = visitorHub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Presence sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var current = _tracker.SnapshotAll();
        var changed = new List<(Guid AgentId, PresenceStatus Status)>();

        foreach (var (agentId, status) in current)
        {
            if (!_lastBroadcast.TryGetValue(agentId, out var previous) || previous != status)
            {
                _lastBroadcast[agentId] = status;
                changed.Add((agentId, status));
            }
        }

        foreach (var (agentId, status) in changed)
        {
            await _agentHub.Clients.Group("agents")
                .SendAsync("presenceChanged", new { agentUserId = agentId, status = status.ToString() }, ct);
        }

        var anyOnline = current.Values.Any(s => s == PresenceStatus.Online);
        if (anyOnline != _lastAnyOnline)
        {
            _lastAnyOnline = anyOnline;
            await _visitorHub.Clients.Group("visitors").SendAsync("teamPresence", new { anyAgentOnline = anyOnline }, ct);
        }

        if (changed.Count > 0)
        {
            await PersistAsync(changed, ct);
        }
    }

    private async Task PersistAsync(IReadOnlyList<(Guid AgentId, PresenceStatus Status)> changed, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        foreach (var (agentId, status) in changed)
        {
            var snapshot = await db.PresenceSnapshots.FirstOrDefaultAsync(p => p.AgentUserId == agentId, ct);
            if (snapshot is null)
            {
                snapshot = new PresenceSnapshot { AgentUserId = agentId };
                db.PresenceSnapshots.Add(snapshot);
            }

            snapshot.Status = status;
            snapshot.LastActivityAtUtc = _tracker.GetLastActivity(agentId);
            snapshot.ChangedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
