using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetIntercom.Application.Common;
using NetIntercom.Application.Conversations;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;

namespace NetIntercom.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/conversations")]
[Authorize(Policy = "Agent")]
public class ConversationsController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public ConversationsController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    /// <summary>Inbox list. Filters: state, teamId, assigneeId, unassigned, search; sort: lastMessage|created|slaBreach.</summary>
    [HttpGet]
    public Task<PaginatedList<ConversationDto>> List(
        [FromQuery] ConversationState? state,
        [FromQuery] Guid? teamId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool? unassigned,
        [FromQuery] string? search,
        [FromQuery] string sort = "lastMessage",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25) =>
        _sender.Send(new ListConversationsQuery(state, teamId, assigneeId, unassigned, search, sort, page, pageSize));

    [HttpGet("{id:guid}")]
    public Task<ConversationDto> Get(Guid id) => _sender.Send(new GetConversationQuery(id));

    /// <summary>Transcript including internal notes. Pass `after` for reconnect catch-up.</summary>
    [HttpGet("{id:guid}/messages")]
    public Task<IReadOnlyList<MessageDto>> Messages(Guid id, [FromQuery] DateTime? after) =>
        _sender.Send(new GetConversationMessagesQuery(id, after));

    public record SendMessageRequest(string Body, bool IsInternalNote = false);

    [HttpPost("{id:guid}/messages")]
    public Task<MessageDto> Send(Guid id, SendMessageRequest request) =>
        _sender.Send(new SendAgentMessageCommand(id, _currentUser.AgentUserId!.Value, request.Body, request.IsInternalNote));

    public record AssignRequest(Guid? AgentUserId, Guid? TeamId);

    [HttpPut("{id:guid}/assignee")]
    public Task<ConversationDto> Assign(Guid id, AssignRequest request) =>
        _sender.Send(new AssignConversationCommand(id, request.AgentUserId, request.TeamId));

    public record ChangeStateRequest(ConversationState State, DateTime? SnoozeUntilUtc);

    [HttpPut("{id:guid}/state")]
    public Task<ConversationDto> ChangeState(Guid id, ChangeStateRequest request) =>
        _sender.Send(new ChangeConversationStateCommand(id, request.State, request.SnoozeUntilUtc));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        await _sender.Send(new MarkReadByAgentCommand(id));
        return NoContent();
    }
}
