using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Application.Routing;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Widget;

public record WidgetSession(string VisitorToken, VisitorDto Visitor, ConversationDto? Conversation, IReadOnlyList<MessageDto> Messages);

/// <summary>
/// Called by the widget on load. Creates or resumes the visitor identity (cookie AnonymousId),
/// optionally identifies them, and returns the latest conversation with history plus a
/// visitor-scoped token for the REST API and SignalR hub.
/// </summary>
public record StartWidgetSessionCommand(
    string AnonymousId,
    string? Name,
    string? Email,
    string? ExternalId,
    string? AttributesJson) : IRequest<WidgetSession>;

public class StartWidgetSessionValidator : AbstractValidator<StartWidgetSessionCommand>
{
    public StartWidgetSessionValidator()
    {
        RuleFor(x => x.AnonymousId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.Name).MaximumLength(200);
    }
}

public class StartWidgetSessionHandler : IRequestHandler<StartWidgetSessionCommand, WidgetSession>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public StartWidgetSessionHandler(IAppDbContext db, ITokenService tokens, IClock clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<WidgetSession> Handle(StartWidgetSessionCommand request, CancellationToken ct)
    {
        var visitor = await _db.Visitors.FirstOrDefaultAsync(
            v => request.ExternalId != null && v.ExternalId == request.ExternalId || v.AnonymousId == request.AnonymousId, ct);

        if (visitor is null)
        {
            visitor = new Visitor { AnonymousId = request.AnonymousId, CreatedAtUtc = _clock.UtcNow };
            _db.Visitors.Add(visitor);
        }

        visitor.Name = request.Name ?? visitor.Name;
        visitor.Email = request.Email ?? visitor.Email;
        visitor.ExternalId = request.ExternalId ?? visitor.ExternalId;
        visitor.AttributesJson = request.AttributesJson ?? visitor.AttributesJson;
        visitor.LastSeenAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        var conversation = await _db.Conversations
            .Include(c => c.Visitor)
            .Include(c => c.Team)
            .Include(c => c.Assignee)
            .Include(c => c.SlaTimers)
            .Where(c => c.VisitorId == visitor.Id)
            .OrderByDescending(c => c.LastMessageAtUtc)
            .FirstOrDefaultAsync(ct);

        var messages = new List<MessageDto>();
        if (conversation is not null)
        {
            messages = await _db.Messages
                .Include(m => m.AgentUser)
                .Where(m => m.ConversationId == conversation.Id && !m.IsInternalNote)
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => MessageDto.From(m, m.SenderType == SenderType.Agent
                    ? m.AgentUser!.DisplayName
                    : m.Conversation.Visitor.Name ?? "You"))
                .ToListAsync(ct);
        }

        return new WidgetSession(
            _tokens.CreateVisitorToken(visitor),
            VisitorDto.From(visitor),
            conversation is null ? null : ConversationDto.From(conversation),
            messages);
    }
}
