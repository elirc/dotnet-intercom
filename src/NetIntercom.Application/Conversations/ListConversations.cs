using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;

namespace NetIntercom.Application.Conversations;

/// <summary>Inbox list with filtering, sorting and pagination conventions shared by all list endpoints.</summary>
public record ListConversationsQuery(
    ConversationState? State,
    Guid? TeamId,
    Guid? AssigneeId,
    bool? Unassigned,
    string? Search,
    string Sort = "lastMessage",
    int Page = 1,
    int PageSize = 25) : IRequest<PaginatedList<ConversationDto>>;

public class ListConversationsHandler : IRequestHandler<ListConversationsQuery, PaginatedList<ConversationDto>>
{
    private readonly IAppDbContext _db;

    public ListConversationsHandler(IAppDbContext db) => _db = db;

    public async Task<PaginatedList<ConversationDto>> Handle(ListConversationsQuery request, CancellationToken ct)
    {
        var query = _db.Conversations
            .Include(c => c.Visitor)
            .Include(c => c.Team)
            .Include(c => c.Assignee)
            .Include(c => c.SlaTimers)
            .AsQueryable();

        if (request.State is not null)
        {
            query = query.Where(c => c.State == request.State);
        }

        if (request.TeamId is not null)
        {
            query = query.Where(c => c.TeamId == request.TeamId);
        }

        if (request.AssigneeId is not null)
        {
            query = query.Where(c => c.AssigneeId == request.AssigneeId);
        }

        if (request.Unassigned == true)
        {
            query = query.Where(c => c.AssigneeId == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.ToLower();
            query = query.Where(c =>
                (c.Subject ?? "").ToLower().Contains(term)
                || (c.Visitor.Name ?? "").ToLower().Contains(term)
                || (c.Visitor.Email ?? "").ToLower().Contains(term));
        }

        query = request.Sort switch
        {
            "created" => query.OrderByDescending(c => c.CreatedAtUtc),
            "slaBreach" => query
                .OrderBy(c => c.SlaTimers
                    .Where(t => t.Status == SlaTimerStatus.Running)
                    .Min(t => (DateTime?)t.BreachAtUtc) ?? DateTime.MaxValue),
            _ => query.OrderByDescending(c => c.LastMessageAtUtc)
        };

        var page = await PaginatedList<Domain.Entities.Conversation>.CreateAsync(query, request.Page, request.PageSize, ct);
        return new PaginatedList<ConversationDto>(
            page.Items.Select(ConversationDto.From).ToList(), page.TotalCount, page.Page, page.PageSize);
    }
}
