using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

/// <summary>
/// Last known presence per agent, persisted by the presence sweeper so status
/// survives API restarts. Live presence is tracked in memory from hub connections.
/// </summary>
public class PresenceSnapshot : Entity
{
    public Guid AgentUserId { get; set; }
    public AgentUser AgentUser { get; set; } = null!;

    public PresenceStatus Status { get; set; }
    public DateTime LastActivityAtUtc { get; set; }
    public DateTime ChangedAtUtc { get; set; }
}
