using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Conversations;

public record GetConversationQuery(Guid ConversationId) : IRequest<ConversationDto>;

public class GetConversationHandler : IRequestHandler<GetConversationQuery, ConversationDto>
{
    private readonly IAppDbContext _db;

    public GetConversationHandler(IAppDbContext db) => _db = db;

    public async Task<ConversationDto> Handle(GetConversationQuery request, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Include(c => c.Visitor)
            .Include(c => c.Team)
            .Include(c => c.Assignee)
            .Include(c => c.SlaTimers)
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId, ct)
            ?? throw new NotFoundException(nameof(Conversation), request.ConversationId);

        return ConversationDto.From(conversation);
    }
}

/// <summary>Agent view of the transcript, internal notes included. `AfterUtc` supports reconnect catch-up.</summary>
public record GetConversationMessagesQuery(Guid ConversationId, DateTime? AfterUtc) : IRequest<IReadOnlyList<MessageDto>>;

public class GetConversationMessagesHandler : IRequestHandler<GetConversationMessagesQuery, IReadOnlyList<MessageDto>>
{
    private readonly IAppDbContext _db;

    public GetConversationMessagesHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MessageDto>> Handle(GetConversationMessagesQuery request, CancellationToken ct)
    {
        var exists = await _db.Conversations.AnyAsync(c => c.Id == request.ConversationId, ct);
        if (!exists)
        {
            throw new NotFoundException(nameof(Conversation), request.ConversationId);
        }

        return await _db.Messages
            .Include(m => m.AgentUser)
            .Include(m => m.Conversation).ThenInclude(c => c.Visitor)
            .Where(m => m.ConversationId == request.ConversationId
                        && (request.AfterUtc == null || m.CreatedAtUtc > request.AfterUtc))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => MessageDto.From(m, m.SenderType == SenderType.Agent
                ? m.AgentUser!.DisplayName
                : m.Conversation.Visitor.Name ?? m.Conversation.Visitor.Email ?? "Visitor"))
            .ToListAsync(ct);
    }
}
