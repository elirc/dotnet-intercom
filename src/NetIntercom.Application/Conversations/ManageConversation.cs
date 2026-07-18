using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Conversations;

/// <summary>Manual (re)assignment to an agent and/or team. Null agent unassigns back to the team queue.</summary>
public record AssignConversationCommand(Guid ConversationId, Guid? AgentUserId, Guid? TeamId) : IRequest<ConversationDto>;

public class AssignConversationHandler : IRequestHandler<AssignConversationCommand, ConversationDto>
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;

    public AssignConversationHandler(IAppDbContext db, IRealtimeNotifier notifier, IClock clock)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<ConversationDto> Handle(AssignConversationCommand request, CancellationToken ct)
    {
        var conversation = await LoadAsync(_db, request.ConversationId, ct);

        if (request.AgentUserId is not null)
        {
            var exists = await _db.AgentUsers.AnyAsync(a => a.Id == request.AgentUserId && a.IsActive, ct);
            if (!exists)
            {
                throw new NotFoundException(nameof(AgentUser), request.AgentUserId);
            }
        }

        conversation.Assign(request.AgentUserId, request.TeamId);

        if (request.AgentUserId is not null && conversation.TeamId is not null)
        {
            var member = await _db.TeamMembers.FirstOrDefaultAsync(
                m => m.TeamId == conversation.TeamId && m.AgentUserId == request.AgentUserId, ct);
            if (member is not null)
            {
                member.LastAssignedAtUtc = _clock.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
        var dto = await ReloadDtoAsync(_db, conversation.Id, ct);
        await _notifier.ConversationUpdatedAsync(dto, ct);
        return dto;
    }

    internal static async Task<Conversation> LoadAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Conversations
            .Include(c => c.Visitor)
            .Include(c => c.Team)
            .Include(c => c.Assignee)
            .Include(c => c.SlaTimers)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException(nameof(Conversation), id);

    internal static async Task<ConversationDto> ReloadDtoAsync(IAppDbContext db, Guid id, CancellationToken ct) =>
        ConversationDto.From(await LoadAsync(db, id, ct));
}

public record ChangeConversationStateCommand(Guid ConversationId, ConversationState State, DateTime? SnoozeUntilUtc) : IRequest<ConversationDto>;

public class ChangeConversationStateValidator : AbstractValidator<ChangeConversationStateCommand>
{
    public ChangeConversationStateValidator()
    {
        RuleFor(x => x.SnoozeUntilUtc)
            .NotNull()
            .When(x => x.State == ConversationState.Snoozed)
            .WithMessage("SnoozeUntilUtc is required when snoozing.");
    }
}

public class ChangeConversationStateHandler : IRequestHandler<ChangeConversationStateCommand, ConversationDto>
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;

    public ChangeConversationStateHandler(IAppDbContext db, IRealtimeNotifier notifier, IClock clock)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task<ConversationDto> Handle(ChangeConversationStateCommand request, CancellationToken ct)
    {
        var conversation = await AssignConversationHandler.LoadAsync(_db, request.ConversationId, ct);
        var now = _clock.UtcNow;

        switch (request.State)
        {
            case ConversationState.Closed:
                conversation.Close(now);
                break;
            case ConversationState.Snoozed:
                conversation.Snooze(request.SnoozeUntilUtc!.Value, now);
                break;
            case ConversationState.Open:
                conversation.Reopen();
                break;
        }

        await _db.SaveChangesAsync(ct);
        var dto = ConversationDto.From(conversation);
        await _notifier.ConversationUpdatedAsync(dto, ct);
        return dto;
    }
}

/// <summary>Marks visitor messages as read by an agent (read receipts to the widget).</summary>
public record MarkReadByAgentCommand(Guid ConversationId) : IRequest;

public class MarkReadByAgentHandler : IRequestHandler<MarkReadByAgentCommand>
{
    private readonly IAppDbContext _db;
    private readonly IRealtimeNotifier _notifier;
    private readonly IClock _clock;

    public MarkReadByAgentHandler(IAppDbContext db, IRealtimeNotifier notifier, IClock clock)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
    }

    public async Task Handle(MarkReadByAgentCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var unread = await _db.Messages
            .Where(m => m.ConversationId == request.ConversationId
                        && m.SenderType == SenderType.Visitor
                        && m.ReadByAgentAtUtc == null)
            .ToListAsync(ct);

        if (unread.Count == 0)
        {
            return;
        }

        foreach (var message in unread)
        {
            message.ReadByAgentAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);
        await _notifier.MessagesReadAsync(request.ConversationId, SenderType.Agent, now, ct);
    }
}
