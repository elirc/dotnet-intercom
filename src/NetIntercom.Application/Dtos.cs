using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Dtos;

public record VisitorDto(Guid Id, string AnonymousId, string? Name, string? Email, string DisplayName, DateTime LastSeenAtUtc, string? AttributesJson)
{
    public static VisitorDto From(Visitor v) =>
        new(v.Id, v.AnonymousId, v.Name, v.Email, v.DisplayName, v.LastSeenAtUtc, v.AttributesJson);
}

public record AgentDto(Guid Id, string Email, string DisplayName, bool IsAdmin, PresenceStatus Presence)
{
    public static AgentDto From(AgentUser a, PresenceStatus presence = PresenceStatus.Offline) =>
        new(a.Id, a.Email, a.DisplayName, a.IsAdmin, presence);
}

public record TeamDto(Guid Id, string Name, bool IsFallback, Guid? DefaultSlaPolicyId, IReadOnlyList<AgentDto> Members)
{
    public static TeamDto From(Team t, Func<Guid, PresenceStatus>? presence = null) =>
        new(t.Id, t.Name, t.IsFallback, t.DefaultSlaPolicyId,
            t.Members.Select(m => AgentDto.From(m.AgentUser, presence?.Invoke(m.AgentUserId) ?? PresenceStatus.Offline)).ToList());
}

public record MessageDto(
    Guid Id,
    Guid ConversationId,
    SenderType SenderType,
    Guid? AgentUserId,
    string SenderName,
    string Body,
    bool IsInternalNote,
    DateTime CreatedAtUtc,
    DateTime? ReadByVisitorAtUtc,
    DateTime? ReadByAgentAtUtc)
{
    public static MessageDto From(Message m, string senderName) =>
        new(m.Id, m.ConversationId, m.SenderType, m.AgentUserId, senderName, m.Body,
            m.IsInternalNote, m.CreatedAtUtc, m.ReadByVisitorAtUtc, m.ReadByAgentAtUtc);
}

public record SlaTimerDto(Guid Id, SlaTimerKind Kind, SlaTimerStatus Status, DateTime StartedAtUtc, DateTime BreachAtUtc, DateTime? CompletedAtUtc)
{
    public static SlaTimerDto From(SlaTimer t) =>
        new(t.Id, t.Kind, t.Status, t.StartedAtUtc, t.BreachAtUtc, t.CompletedAtUtc);
}

public record ConversationDto(
    Guid Id,
    ConversationState State,
    string? Subject,
    string? SourceUrl,
    VisitorDto Visitor,
    Guid? TeamId,
    string? TeamName,
    Guid? AssigneeId,
    string? AssigneeName,
    DateTime CreatedAtUtc,
    DateTime LastMessageAtUtc,
    string? LastMessagePreview,
    DateTime? FirstAgentReplyAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? SnoozedUntilUtc,
    SlaTimerDto? ActiveSlaTimer,
    bool HasBreachedSla)
{
    public static ConversationDto From(Conversation c) =>
        new(c.Id, c.State, c.Subject, c.SourceUrl, VisitorDto.From(c.Visitor),
            c.TeamId, c.Team?.Name, c.AssigneeId, c.Assignee?.DisplayName,
            c.CreatedAtUtc, c.LastMessageAtUtc, c.LastMessagePreview,
            c.FirstAgentReplyAtUtc, c.ClosedAtUtc, c.SnoozedUntilUtc,
            c.SlaTimers.Where(t => t.Status == SlaTimerStatus.Running)
                .OrderBy(t => t.BreachAtUtc)
                .Select(SlaTimerDto.From)
                .FirstOrDefault(),
            c.SlaTimers.Any(t => t.Status == SlaTimerStatus.Breached));
}

public record CannedReplyDto(Guid Id, string Shortcut, string Title, string Body, Guid? TeamId, Guid? AgentUserId)
{
    public static CannedReplyDto From(CannedReply r) =>
        new(r.Id, r.Shortcut, r.Title, r.Body, r.TeamId, r.AgentUserId);
}

public record SlaPolicyDto(Guid Id, string Name, int FirstResponseMinutes, int NextResponseMinutes)
{
    public static SlaPolicyDto From(SlaPolicy p) => new(p.Id, p.Name, p.FirstResponseMinutes, p.NextResponseMinutes);
}

public record RoutingRuleDto(Guid Id, string Name, int Priority, string? Keyword, bool IsEnabled, Guid TeamId, string TeamName)
{
    public static RoutingRuleDto From(RoutingRule r) =>
        new(r.Id, r.Name, r.Priority, r.Keyword, r.IsEnabled, r.TeamId, r.Team?.Name ?? string.Empty);
}
