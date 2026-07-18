using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

public class Message : AuditableEntity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public SenderType SenderType { get; set; }
    public Guid? AgentUserId { get; set; }
    public AgentUser? AgentUser { get; set; }
    public Guid? VisitorId { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>Internal notes are visible to agents only and never delivered to the visitor.</summary>
    public bool IsInternalNote { get; set; }

    public DateTime? ReadByVisitorAtUtc { get; set; }
    public DateTime? ReadByAgentAtUtc { get; set; }
}
