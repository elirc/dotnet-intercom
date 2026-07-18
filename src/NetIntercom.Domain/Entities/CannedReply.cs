using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

/// <summary>Team-shared (TeamId set) or personal (AgentUserId set) saved reply, inserted via /shortcut.</summary>
public class CannedReply : AuditableEntity, ISoftDeletable
{
    public string Shortcut { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    /// <summary>May contain placeholders such as {{visitor.name}} and {{agent.name}}.</summary>
    public string Body { get; set; } = string.Empty;

    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
    public Guid? AgentUserId { get; set; }
    public AgentUser? AgentUser { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}
