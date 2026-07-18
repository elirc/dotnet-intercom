using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

public class Conversation : AuditableEntity
{
    public Guid VisitorId { get; set; }
    public Visitor Visitor { get; set; } = null!;

    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    public Guid? AssigneeId { get; set; }
    public AgentUser? Assignee { get; set; }

    public Guid? SlaPolicyId { get; set; }
    public SlaPolicy? SlaPolicy { get; set; }

    public ConversationState State { get; private set; } = ConversationState.Open;
    public string? Subject { get; set; }
    public string? SourceUrl { get; set; }

    public DateTime? FirstAgentReplyAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? SnoozedUntilUtc { get; private set; }

    public DateTime LastMessageAtUtc { get; set; }
    public string? LastMessagePreview { get; set; }

    /// <summary>Optimistic concurrency token mapped to PostgreSQL xmin.</summary>
    public uint RowVersion { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<SlaTimer> SlaTimers { get; set; } = new List<SlaTimer>();

    public void Assign(Guid? agentUserId, Guid? teamId)
    {
        if (State == ConversationState.Closed)
        {
            throw new DomainException("Cannot assign a closed conversation. Reopen it first.");
        }

        AssigneeId = agentUserId;
        if (teamId is not null)
        {
            TeamId = teamId;
        }
    }

    public void Close(DateTime nowUtc)
    {
        if (State == ConversationState.Closed)
        {
            throw new DomainException("Conversation is already closed.");
        }

        State = ConversationState.Closed;
        ClosedAtUtc = nowUtc;
        SnoozedUntilUtc = null;
        CancelRunningTimers(nowUtc);
    }

    public void Snooze(DateTime untilUtc, DateTime nowUtc)
    {
        if (State == ConversationState.Closed)
        {
            throw new DomainException("Cannot snooze a closed conversation.");
        }

        if (untilUtc <= nowUtc)
        {
            throw new DomainException("Snooze time must be in the future.");
        }

        State = ConversationState.Snoozed;
        SnoozedUntilUtc = untilUtc;
    }

    public void Reopen()
    {
        State = ConversationState.Open;
        ClosedAtUtc = null;
        SnoozedUntilUtc = null;
    }

    /// <summary>Records a public agent reply: completes running SLA timers and stamps first response.</summary>
    public void RecordAgentReply(DateTime nowUtc)
    {
        FirstAgentReplyAtUtc ??= nowUtc;
        foreach (var timer in SlaTimers.Where(t => t.Status == SlaTimerStatus.Running))
        {
            timer.MarkMet(nowUtc);
        }

        // A visitor waiting on a snoozed conversation gets it back when the agent replies.
        if (State == ConversationState.Snoozed)
        {
            Reopen();
        }
    }

    /// <summary>Records an inbound visitor message: reopens if needed and starts a next-response timer.</summary>
    public void RecordVisitorMessage(DateTime nowUtc)
    {
        if (State != ConversationState.Open)
        {
            Reopen();
        }

        if (SlaPolicy is null)
        {
            return;
        }

        var hasRunningTimer = SlaTimers.Any(t => t.Status == SlaTimerStatus.Running);
        if (hasRunningTimer)
        {
            return;
        }

        var kind = FirstAgentReplyAtUtc is null ? SlaTimerKind.FirstResponse : SlaTimerKind.NextResponse;
        var minutes = kind == SlaTimerKind.FirstResponse
            ? SlaPolicy.FirstResponseMinutes
            : SlaPolicy.NextResponseMinutes;

        SlaTimers.Add(SlaTimer.Start(Id, kind, nowUtc, TimeSpan.FromMinutes(minutes)));
    }

    private void CancelRunningTimers(DateTime nowUtc)
    {
        foreach (var timer in SlaTimers.Where(t => t.Status == SlaTimerStatus.Running))
        {
            timer.Cancel(nowUtc);
        }
    }
}
