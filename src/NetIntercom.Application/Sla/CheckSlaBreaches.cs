using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;

namespace NetIntercom.Application.Sla;

/// <summary>
/// Invoked by the recurring Hangfire job. Timers live in the DB so breach detection
/// survives app restarts; this scan marks overdue timers breached and notifies agents.
/// </summary>
public record CheckSlaBreachesCommand : IRequest<int>;

public class CheckSlaBreachesHandler : IRequestHandler<CheckSlaBreachesCommand, int>
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<CheckSlaBreachesHandler> _logger;

    public CheckSlaBreachesHandler(IAppDbContext db, IRealtimeNotifier notifier, IClock clock, ILogger<CheckSlaBreachesHandler> logger)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(CheckSlaBreachesCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var overdue = await _db.SlaTimers
            .Include(t => t.Conversation).ThenInclude(c => c.Visitor)
            .Include(t => t.Conversation).ThenInclude(c => c.Team)
            .Include(t => t.Conversation).ThenInclude(c => c.Assignee)
            .Include(t => t.Conversation).ThenInclude(c => c.SlaTimers)
            .Where(t => t.Status == SlaTimerStatus.Running && t.BreachAtUtc <= now)
            .ToListAsync(ct);

        foreach (var timer in overdue)
        {
            timer.MarkBreached(now);
            _logger.LogWarning(
                "SLA breach: conversation {ConversationId}, {Kind} timer due {BreachAt:u} (team {Team}, assignee {Assignee})",
                timer.ConversationId, timer.Kind, timer.BreachAtUtc,
                timer.Conversation.Team?.Name ?? "-", timer.Conversation.Assignee?.DisplayName ?? "unassigned");
        }

        if (overdue.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var timer in overdue)
            {
                // Escalation: pushed to the assignee's team and the global inbox stream.
                await _notifier.SlaBreachedAsync(ConversationDto.From(timer.Conversation), timer.Kind, ct);
            }
        }

        return overdue.Count;
    }
}
