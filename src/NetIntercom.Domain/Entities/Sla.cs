using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

public class SlaPolicy : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int FirstResponseMinutes { get; set; }
    public int NextResponseMinutes { get; set; }
}

public class SlaTimer : Entity
{
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public SlaTimerKind Kind { get; set; }
    public SlaTimerStatus Status { get; private set; } = SlaTimerStatus.Running;
    public DateTime StartedAtUtc { get; set; }
    public DateTime BreachAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static SlaTimer Start(Guid conversationId, SlaTimerKind kind, DateTime nowUtc, TimeSpan window) => new()
    {
        ConversationId = conversationId,
        Kind = kind,
        StartedAtUtc = nowUtc,
        BreachAtUtc = nowUtc.Add(window)
    };

    public void MarkMet(DateTime nowUtc)
    {
        if (Status != SlaTimerStatus.Running)
        {
            throw new DomainException("Only a running SLA timer can be met.");
        }

        Status = SlaTimerStatus.Met;
        CompletedAtUtc = nowUtc;
    }

    public void MarkBreached(DateTime nowUtc)
    {
        if (Status != SlaTimerStatus.Running)
        {
            throw new DomainException("Only a running SLA timer can breach.");
        }

        Status = SlaTimerStatus.Breached;
        CompletedAtUtc = nowUtc;
    }

    public void Cancel(DateTime nowUtc)
    {
        if (Status != SlaTimerStatus.Running)
        {
            return;
        }

        Status = SlaTimerStatus.Cancelled;
        CompletedAtUtc = nowUtc;
    }
}
