using System.Collections.Concurrent;
using NetIntercom.Application.Common;
using NetIntercom.Domain;

namespace NetIntercom.Api.Realtime;

/// <summary>
/// In-memory presence derived from hub connections and activity heartbeats.
/// Grace periods avoid flapping: a brief disconnect (refresh, network blip) keeps the
/// agent online; idle connections degrade to Away. Durable state is persisted by
/// <see cref="PresenceSweeper"/> as PresenceSnapshot rows.
/// </summary>
public class PresenceTracker : IPresenceService
{
    public static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AwayAfterIdle = TimeSpan.FromMinutes(5);

    private sealed class AgentState
    {
        public ConcurrentDictionary<string, byte> Connections { get; } = new();
        public DateTime LastActivityUtc { get; set; }
        public DateTime LastDisconnectUtc { get; set; }
    }

    private readonly ConcurrentDictionary<Guid, AgentState> _agents = new();

    // conversationId -> connectionId -> (agentId, displayName); used for collision detection.
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, (Guid AgentId, string Name)>> _viewers = new();

    public void Connected(Guid agentId, string connectionId)
    {
        var state = _agents.GetOrAdd(agentId, _ => new AgentState());
        state.Connections[connectionId] = 1;
        state.LastActivityUtc = DateTime.UtcNow;
    }

    public void Disconnected(Guid agentId, string connectionId)
    {
        if (_agents.TryGetValue(agentId, out var state))
        {
            state.Connections.TryRemove(connectionId, out _);
            state.LastDisconnectUtc = DateTime.UtcNow;
        }

        foreach (var viewers in _viewers.Values)
        {
            viewers.TryRemove(connectionId, out _);
        }
    }

    public void Heartbeat(Guid agentId)
    {
        if (_agents.TryGetValue(agentId, out var state))
        {
            state.LastActivityUtc = DateTime.UtcNow;
        }
    }

    public PresenceStatus GetStatus(Guid agentUserId) => Compute(agentUserId, DateTime.UtcNow);

    public IReadOnlyCollection<Guid> GetOnlineAgentIds()
    {
        var now = DateTime.UtcNow;
        return _agents.Keys.Where(id => Compute(id, now) == PresenceStatus.Online).ToList();
    }

    public IReadOnlyDictionary<Guid, PresenceStatus> SnapshotAll()
    {
        var now = DateTime.UtcNow;
        return _agents.Keys.ToDictionary(id => id, id => Compute(id, now));
    }

    public DateTime GetLastActivity(Guid agentId) =>
        _agents.TryGetValue(agentId, out var s) ? s.LastActivityUtc : DateTime.MinValue;

    private PresenceStatus Compute(Guid agentId, DateTime now)
    {
        if (!_agents.TryGetValue(agentId, out var state))
        {
            return PresenceStatus.Offline;
        }

        var connected = !state.Connections.IsEmpty;
        if (!connected && now - state.LastDisconnectUtc > DisconnectGrace)
        {
            return PresenceStatus.Offline;
        }

        return now - state.LastActivityUtc > AwayAfterIdle ? PresenceStatus.Away : PresenceStatus.Online;
    }

    // ----- conversation viewers (collision detection) -----

    public IReadOnlyList<(Guid AgentId, string Name)> AddViewer(Guid conversationId, string connectionId, Guid agentId, string name)
    {
        var viewers = _viewers.GetOrAdd(conversationId, _ => new ConcurrentDictionary<string, (Guid, string)>());
        viewers[connectionId] = (agentId, name);
        return GetViewers(conversationId);
    }

    public void RemoveViewer(Guid conversationId, string connectionId)
    {
        if (_viewers.TryGetValue(conversationId, out var viewers))
        {
            viewers.TryRemove(connectionId, out _);
        }
    }

    /// <summary>Drops every viewer entry for a connection (on disconnect); returns affected conversations.</summary>
    public IReadOnlyList<Guid> RemoveAllViewersForConnection(string connectionId)
    {
        var affected = new List<Guid>();
        foreach (var (conversationId, viewers) in _viewers)
        {
            if (viewers.TryRemove(connectionId, out _))
            {
                affected.Add(conversationId);
            }
        }

        return affected;
    }

    public IReadOnlyList<(Guid AgentId, string Name)> GetViewers(Guid conversationId) =>
        _viewers.TryGetValue(conversationId, out var viewers)
            ? viewers.Values.DistinctBy(v => v.AgentId).Select(v => (v.AgentId, v.Name)).ToList()
            : Array.Empty<(Guid, string)>();
}
