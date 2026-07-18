using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.CannedReplies;

/// <summary>Replies visible to an agent: their personal ones plus those shared with their teams.</summary>
public record ListCannedRepliesQuery(Guid AgentUserId, string? Search) : IRequest<IReadOnlyList<CannedReplyDto>>;

public class ListCannedRepliesHandler : IRequestHandler<ListCannedRepliesQuery, IReadOnlyList<CannedReplyDto>>
{
    private readonly IAppDbContext _db;

    public ListCannedRepliesHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<CannedReplyDto>> Handle(ListCannedRepliesQuery request, CancellationToken ct)
    {
        var teamIds = await _db.TeamMembers
            .Where(m => m.AgentUserId == request.AgentUserId)
            .Select(m => m.TeamId)
            .ToListAsync(ct);

        var query = _db.CannedReplies.Where(r =>
            r.AgentUserId == request.AgentUserId
            || (r.TeamId != null && teamIds.Contains(r.TeamId.Value)));

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.ToLower();
            query = query.Where(r => r.Shortcut.ToLower().Contains(term) || r.Title.ToLower().Contains(term));
        }

        return await query
            .OrderBy(r => r.Shortcut)
            .Select(r => CannedReplyDto.From(r))
            .ToListAsync(ct);
    }
}

public record SaveCannedReplyCommand(
    Guid? Id,
    Guid AgentUserId,
    string Shortcut,
    string Title,
    string Body,
    Guid? TeamId,
    bool Personal) : IRequest<CannedReplyDto>;

public class SaveCannedReplyValidator : AbstractValidator<SaveCannedReplyCommand>
{
    public SaveCannedReplyValidator()
    {
        RuleFor(x => x.Shortcut).NotEmpty().MaximumLength(50).Matches("^[a-zA-Z0-9_-]+$")
            .WithMessage("Shortcut may contain letters, digits, '-' and '_' only.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10_000);
        RuleFor(x => x.TeamId).NotNull().When(x => !x.Personal)
            .WithMessage("Team replies require a TeamId.");
    }
}

public class SaveCannedReplyHandler : IRequestHandler<SaveCannedReplyCommand, CannedReplyDto>
{
    private readonly IAppDbContext _db;

    public SaveCannedReplyHandler(IAppDbContext db) => _db = db;

    public async Task<CannedReplyDto> Handle(SaveCannedReplyCommand request, CancellationToken ct)
    {
        CannedReply reply;
        if (request.Id is not null)
        {
            reply = await _db.CannedReplies.FirstOrDefaultAsync(r => r.Id == request.Id, ct)
                    ?? throw new NotFoundException(nameof(CannedReply), request.Id);
            if (reply.AgentUserId is not null && reply.AgentUserId != request.AgentUserId)
            {
                throw new ForbiddenException("You can only edit your own personal replies.");
            }
        }
        else
        {
            reply = new CannedReply();
            _db.CannedReplies.Add(reply);
        }

        reply.Shortcut = request.Shortcut;
        reply.Title = request.Title;
        reply.Body = request.Body;
        reply.TeamId = request.Personal ? null : request.TeamId;
        reply.AgentUserId = request.Personal ? request.AgentUserId : null;

        await _db.SaveChangesAsync(ct);
        return CannedReplyDto.From(reply);
    }
}

public record DeleteCannedReplyCommand(Guid Id, Guid AgentUserId, bool IsAdmin) : IRequest;

public class DeleteCannedReplyHandler : IRequestHandler<DeleteCannedReplyCommand>
{
    private readonly IAppDbContext _db;

    public DeleteCannedReplyHandler(IAppDbContext db) => _db = db;

    public async Task Handle(DeleteCannedReplyCommand request, CancellationToken ct)
    {
        var reply = await _db.CannedReplies.FirstOrDefaultAsync(r => r.Id == request.Id, ct)
                    ?? throw new NotFoundException(nameof(CannedReply), request.Id);

        if (!request.IsAdmin && reply.AgentUserId != request.AgentUserId)
        {
            throw new ForbiddenException("Only admins can delete team replies.");
        }

        _db.CannedReplies.Remove(reply); // converted to a soft delete by the interceptor
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Server-side placeholder rendering for a reply against a conversation's context.</summary>
public record RenderCannedReplyQuery(Guid ReplyId, Guid ConversationId, Guid AgentUserId) : IRequest<string>;

public class RenderCannedReplyHandler : IRequestHandler<RenderCannedReplyQuery, string>
{
    private readonly IAppDbContext _db;

    public RenderCannedReplyHandler(IAppDbContext db) => _db = db;

    public async Task<string> Handle(RenderCannedReplyQuery request, CancellationToken ct)
    {
        var reply = await _db.CannedReplies.FirstOrDefaultAsync(r => r.Id == request.ReplyId, ct)
                    ?? throw new NotFoundException(nameof(CannedReply), request.ReplyId);
        var conversation = await _db.Conversations
            .Include(c => c.Visitor)
            .Include(c => c.Team)
            .FirstOrDefaultAsync(c => c.Id == request.ConversationId, ct)
            ?? throw new NotFoundException(nameof(Conversation), request.ConversationId);
        var agent = await _db.AgentUsers.FirstAsync(a => a.Id == request.AgentUserId, ct);

        return PlaceholderRenderer.Render(reply.Body, new Dictionary<string, string?>
        {
            ["visitor.name"] = conversation.Visitor.Name,
            ["visitor.email"] = conversation.Visitor.Email,
            ["agent.name"] = agent.DisplayName,
            ["team.name"] = conversation.Team?.Name
        });
    }
}
