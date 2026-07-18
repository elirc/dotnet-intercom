using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Application.Routing;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Widget;

/// <summary>
/// Sends a visitor message. When <paramref name="ConversationId"/> is null (or the conversation
/// is closed and <paramref name="StartNew"/> is set) a new conversation is created and routed.
/// </summary>
public record SendVisitorMessageCommand(
    Guid VisitorId,
    Guid? ConversationId,
    string Body,
    string? SourceUrl,
    bool StartNew = false) : IRequest<(ConversationDto Conversation, MessageDto Message)>;

public class SendVisitorMessageValidator : AbstractValidator<SendVisitorMessageCommand>
{
    public SendVisitorMessageValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.VisitorId).NotEmpty();
    }
}

public class SendVisitorMessageHandler : IRequestHandler<SendVisitorMessageCommand, (ConversationDto, MessageDto)>
{
    private readonly IAppDbContext _db;
    private readonly ConversationRouter _router;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;

    public SendVisitorMessageHandler(IAppDbContext db, ConversationRouter router, IRealtimeNotifier notifier, IClock clock)
    {
        _db = db;
        _router = router;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<(ConversationDto, MessageDto)> Handle(SendVisitorMessageCommand request, CancellationToken ct)
    {
        var visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.Id == request.VisitorId, ct)
                      ?? throw new NotFoundException(nameof(Visitor), request.VisitorId);

        Conversation? conversation = null;
        if (request.ConversationId is not null)
        {
            conversation = await _db.Conversations
                .Include(c => c.Visitor)
                .Include(c => c.Team)
                .Include(c => c.Assignee)
                .Include(c => c.SlaPolicy)
                .Include(c => c.SlaTimers)
                .FirstOrDefaultAsync(c => c.Id == request.ConversationId && c.VisitorId == visitor.Id, ct)
                ?? throw new NotFoundException(nameof(Conversation), request.ConversationId);

            if (conversation.State == ConversationState.Closed && request.StartNew)
            {
                conversation = null;
            }
        }

        var now = _clock.UtcNow;
        var isNew = conversation is null;
        if (conversation is null)
        {
            conversation = new Conversation
            {
                VisitorId = visitor.Id,
                Visitor = visitor,
                SourceUrl = request.SourceUrl,
                CreatedAtUtc = now,
                Subject = Truncate(request.Body, 80)
            };
            _db.Conversations.Add(conversation);
            await _router.RouteAsync(conversation, request.Body, ct);
            if (conversation.SlaPolicyId is not null)
            {
                conversation.SlaPolicy = await _db.SlaPolicies
                    .FirstOrDefaultAsync(p => p.Id == conversation.SlaPolicyId, ct);
            }
        }

        var message = new Message
        {
            Conversation = conversation,
            ConversationId = conversation.Id,
            SenderType = SenderType.Visitor,
            VisitorId = visitor.Id,
            Body = request.Body,
            CreatedAtUtc = now
        };
        _db.Messages.Add(message);

        conversation.RecordVisitorMessage(now);
        conversation.LastMessageAtUtc = now;
        conversation.LastMessagePreview = Truncate(request.Body, 120);
        visitor.LastSeenAtUtc = now;

        await _db.SaveChangesAsync(ct);

        var conversationDto = ConversationDto.From(conversation);
        var messageDto = MessageDto.From(message, visitor.DisplayName);
        await _notifier.MessageCreatedAsync(messageDto, ct);
        await _notifier.ConversationUpdatedAsync(conversationDto, ct);

        return (conversationDto, messageDto);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
