using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Domain;

namespace NetIntercom.Application.Reports;

public record AgentStatsDto(Guid AgentUserId, string AgentName, int ConversationsHandled, int MessagesSent, double? MedianFirstResponseMinutes);

public record OverviewReportDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int NewConversations,
    int ClosedConversations,
    int OpenConversations,
    double? MedianFirstResponseMinutes,
    double? MedianResolutionMinutes,
    IReadOnlyList<AgentStatsDto> AgentStats);

public record GetOverviewReportQuery(DateTime FromUtc, DateTime ToUtc) : IRequest<OverviewReportDto>;

public class GetOverviewReportHandler : IRequestHandler<GetOverviewReportQuery, OverviewReportDto>
{
    private readonly IAppDbContext _db;

    public GetOverviewReportHandler(IAppDbContext db) => _db = db;

    public async Task<OverviewReportDto> Handle(GetOverviewReportQuery request, CancellationToken ct)
    {
        var conversations = await _db.Conversations
            .Where(c => c.CreatedAtUtc >= request.FromUtc && c.CreatedAtUtc < request.ToUtc)
            .Select(c => new { c.Id, c.CreatedAtUtc, c.FirstAgentReplyAtUtc, c.ClosedAtUtc, c.State })
            .ToListAsync(ct);

        var firstResponses = conversations
            .Where(c => c.FirstAgentReplyAtUtc != null)
            .Select(c => (c.FirstAgentReplyAtUtc!.Value - c.CreatedAtUtc).TotalMinutes)
            .ToList();

        var resolutions = conversations
            .Where(c => c.ClosedAtUtc != null)
            .Select(c => (c.ClosedAtUtc!.Value - c.CreatedAtUtc).TotalMinutes)
            .ToList();

        var agentStats = await _db.Messages
            .Where(m => m.SenderType == SenderType.Agent
                        && !m.IsInternalNote
                        && m.CreatedAtUtc >= request.FromUtc && m.CreatedAtUtc < request.ToUtc)
            .GroupBy(m => new { m.AgentUserId, m.AgentUser!.DisplayName })
            .Select(g => new
            {
                g.Key.AgentUserId,
                g.Key.DisplayName,
                Messages = g.Count(),
                Conversations = g.Select(m => m.ConversationId).Distinct().Count()
            })
            .ToListAsync(ct);

        // Median first response per agent (first public reply attribution).
        var firstReplies = await _db.Messages
            .Where(m => m.SenderType == SenderType.Agent && !m.IsInternalNote)
            .Where(m => m.Conversation.CreatedAtUtc >= request.FromUtc && m.Conversation.CreatedAtUtc < request.ToUtc)
            .GroupBy(m => m.ConversationId)
            .Select(g => g.OrderBy(m => m.CreatedAtUtc)
                .Select(m => new { m.AgentUserId, m.CreatedAtUtc, ConversationCreated = m.Conversation.CreatedAtUtc })
                .First())
            .ToListAsync(ct);

        var perAgentFirstResponse = firstReplies
            .Where(f => f.AgentUserId != null)
            .GroupBy(f => f.AgentUserId!.Value)
            .ToDictionary(g => g.Key, g => Median(g.Select(f => (f.CreatedAtUtc - f.ConversationCreated).TotalMinutes).ToList()));

        return new OverviewReportDto(
            request.FromUtc,
            request.ToUtc,
            conversations.Count,
            conversations.Count(c => c.ClosedAtUtc != null),
            conversations.Count(c => c.State == ConversationState.Open),
            Median(firstResponses),
            Median(resolutions),
            agentStats.Select(a => new AgentStatsDto(
                a.AgentUserId!.Value,
                a.DisplayName,
                a.Conversations,
                a.Messages,
                perAgentFirstResponse.GetValueOrDefault(a.AgentUserId!.Value))).ToList());
    }

    public static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
    }
}

public record SlaTeamReportRow(Guid? TeamId, string TeamName, SlaTimerKind Kind, int Met, int Breached, double MetPercent);

public record GetSlaReportQuery(DateTime FromUtc, DateTime ToUtc) : IRequest<IReadOnlyList<SlaTeamReportRow>>;

public class GetSlaReportHandler : IRequestHandler<GetSlaReportQuery, IReadOnlyList<SlaTeamReportRow>>
{
    private readonly IAppDbContext _db;

    public GetSlaReportHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SlaTeamReportRow>> Handle(GetSlaReportQuery request, CancellationToken ct)
    {
        var rows = await _db.SlaTimers
            .Where(t => t.StartedAtUtc >= request.FromUtc && t.StartedAtUtc < request.ToUtc)
            .Where(t => t.Status == SlaTimerStatus.Met || t.Status == SlaTimerStatus.Breached)
            .GroupBy(t => new { t.Conversation.TeamId, TeamName = t.Conversation.Team!.Name, t.Kind })
            .Select(g => new
            {
                g.Key.TeamId,
                g.Key.TeamName,
                g.Key.Kind,
                Met = g.Count(t => t.Status == SlaTimerStatus.Met),
                Breached = g.Count(t => t.Status == SlaTimerStatus.Breached)
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new SlaTeamReportRow(
                r.TeamId,
                r.TeamName ?? "(unrouted)",
                r.Kind,
                r.Met,
                r.Breached,
                r.Met + r.Breached == 0 ? 100 : Math.Round(100.0 * r.Met / (r.Met + r.Breached), 1)))
            .OrderBy(r => r.TeamName).ThenBy(r => r.Kind)
            .ToList();
    }
}
