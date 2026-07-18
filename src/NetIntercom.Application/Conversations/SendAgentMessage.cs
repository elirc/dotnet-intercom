using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Conversations;

/// <summary>Agent reply or internal note. Notes never reach the visitor and do not touch SLA timers.</summary>
public record SendAgentMessageCommand(Guid ConversationId, Guid AgentUserId, string Body, bool IsInternalNote) : IRequest<MessageDto>;

public class SendAgentMessageValidator : AbstractValidator<SendAgentMessageCommand>
{
    public SendAgentMessageValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.ConversationId).NotEmpty();
    }
}

public class SendAgentMessageHandler : IRequestHandler<SendAgentMessageCommand, MessageDto>
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;

    public SendAgentMessageHandler(IAppDbContext db, IRealtimeNotifier notifier, IClock clock)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<MessageDto> Handle(SendAgentMessageCommand request, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Include(c => c.Visitor)
            .Include(c => c.Team)
            .Include(c => c.Assignee)
            .Include(c => c.SlaTimers)
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId, ct)
            ?? throw new NotFoundException(nameof(Conversation), request.ConversationId);

        var agent = await _db.AgentUsers.FirstAsync(a => a.Id == request.AgentUserId, ct);
        var now = _clock.UtcNow;

        var message = new Message
        {
            ConversationId = conversation.Id,
            SenderType = SenderType.Agent,
            AgentUserId = agent.Id,
            Body = request.Body,
            IsInternalNote = request.IsInternalNote,
            CreatedAtUtc = now
        };
        _db.Messages.Add(message);

        if (!request.IsInternalNote)
        {
            conversation.RecordAgentReply(now);
            conversation.LastMessageAtUtc = now;
            conversation.LastMessagePreview = request.Body.Length <= 120 ? request.Body : request.Body[..120] + "…";
        }

        await _db.SaveChangesAsync(ct);

        var dto = MessageDto.From(message, agent.DisplayName);
        await _notifier.MessageCreatedAsync(dto, ct);
        await _notifier.ConversationUpdatedAsync(ConversationDto.From(conversation), ct);
        return dto;
    }
}
