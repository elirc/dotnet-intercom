using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetIntercom.Application.Common;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Routing;

/// <summary>
/// Assigns a new conversation: first routing rule (by priority) whose keyword matches wins;
/// within the matched team, the online member assigned least recently gets it (round-robin).
/// Outside office hours, or when no rule matches, the conversation lands in the fallback
/// team's queue unassigned. The SLA policy comes from the resolved team's default.
/// </summary>
public class ConversationRouter
{
    private readonly IAppDbContext _db;
    private readonly IPresenceService _presence;
    private readonly IClock _clock;
    private readonly ILogger<ConversationRouter> _logger;

    public ConversationRouter(IAppDbContext db, IPresenceService presence, IClock clock, ILogger<ConversationRouter> logger)
    {
        _db = db;
        _presence = presence;
        _clock = clock;
        _logger = logger;
    }

    public async Task RouteAsync(Conversation conversation, string firstMessage, CancellationToken ct)
    {
        var rules = await _db.RoutingRules
            .Include(r => r.Team)
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        var matched = rules.FirstOrDefault(r => r.Matches(firstMessage, conversation.SourceUrl));
        var team = matched?.Team;

        if (team is not null)
        {
            var officeHours = OfficeHours.FromJson(team.OfficeHoursJson);
            if (officeHours is not null && !officeHours.IsOpenAt(_clock.UtcNow))
            {
                _logger.LogInformation("Team {Team} is outside office hours; falling back", team.Name);
                team = null;
            }
        }

        team ??= await _db.Teams.FirstOrDefaultAsync(t => t.IsFallback, ct)
                 ?? await _db.Teams.OrderBy(t => t.CreatedAtUtc).FirstOrDefaultAsync(ct);

        if (team is null)
        {
            _logger.LogWarning("No teams exist; conversation {ConversationId} left unrouted", conversation.Id);
            return;
        }

        conversation.TeamId = team.Id;
        conversation.SlaPolicyId ??= team.DefaultSlaPolicyId;

        var assignee = await PickRoundRobinAsync(team.Id, ct);
        if (assignee is not null)
        {
            conversation.Assign(assignee.AgentUserId, team.Id);
            assignee.LastAssignedAtUtc = _clock.UtcNow;
        }
        else
        {
            _logger.LogInformation("No online agents in team {Team}; conversation {ConversationId} queued",
                team.Name, conversation.Id);
        }
    }

    private async Task<TeamMember?> PickRoundRobinAsync(Guid teamId, CancellationToken ct)
    {
        var members = await _db.TeamMembers
            .Where(m => m.TeamId == teamId && m.AgentUser.IsActive)
            .ToListAsync(ct);

        var online = _presence.GetOnlineAgentIds();
        return members
            .Where(m => online.Contains(m.AgentUserId))
            .OrderBy(m => m.LastAssignedAtUtc ?? DateTime.MinValue)
            .FirstOrDefault();
    }
}
