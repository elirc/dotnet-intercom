using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Widget;

/// <summary>
/// Missed-message catch-up after a SignalR reconnect: returns public messages created
/// after <paramref name="AfterUtc"/> (or full history when null). The DB is source-of-truth;
/// the hub is delivery only.
/// </summary>
public record GetVisitorMessagesQuery(Guid VisitorId, Guid ConversationId, DateTime? AfterUtc) : IRequest<IReadOnlyList<MessageDto>>;

public class GetVisitorMessagesHandler : IRequestHandler<GetVisitorMessagesQuery, IReadOnlyList<MessageDto>>
{
    private readonly IAppDbContext _db;

    public GetVisitorMessagesHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MessageDto>> Handle(GetVisitorMessagesQuery request, CancellationToken ct)
    {
        var owns = await _db.Conversations
            .AnyAsync(c => c.Id == request.ConversationId && c.VisitorId == request.VisitorId, ct);
        if (!owns)
        {
            throw new ForbiddenException();
        }

        return await _db.Messages
            .Include(m => m.AgentUser)
            .Where(m => m.ConversationId == request.ConversationId
                        && !m.IsInternalNote
                        && (request.AfterUtc == null || m.CreatedAtUtc > request.AfterUtc))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => MessageDto.From(m, m.SenderType == SenderType.Agent ? m.AgentUser!.DisplayName : "You"))
            .ToListAsync(ct);
    }
}

/// <summary>Marks agent messages as read by the visitor (read receipts).</summary>
public record MarkReadByVisitorCommand(Guid VisitorId, Guid ConversationId) : IRequest;

public class MarkReadByVisitorHandler : IRequestHandler<MarkReadByVisitorCommand>
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;

    public MarkReadByVisitorHandler(IAppDbContext db, IRealtimeNotifier notifier, IClock clock)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task Handle(MarkReadByVisitorCommand request, CancellationToken ct)
    {
        var owns = await _db.Conversations
            .AnyAsync(c => c.Id == request.ConversationId && c.VisitorId == request.VisitorId, ct);
        if (!owns)
        {
            throw new ForbiddenException();
        }

        var now = _clock.UtcNow;
        var unread = await _db.Messages
            .Where(m => m.ConversationId == request.ConversationId
                        && m.SenderType == SenderType.Agent
                        && !m.IsInternalNote
                        && m.ReadByVisitorAtUtc == null)
            .ToListAsync(ct);

        if (unread.Count == 0)
        {
            return;
        }

        foreach (var message in unread)
        {
            message.ReadByVisitorAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
        await _notifier.MessagesReadAsync(request.ConversationId, SenderType.Visitor, now, ct);
    }
}
