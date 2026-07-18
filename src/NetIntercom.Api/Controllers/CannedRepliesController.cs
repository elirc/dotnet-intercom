using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetIntercom.Application.CannedReplies;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;

namespace NetIntercom.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/canned-replies")]
[Authorize(Policy = "Agent")]
public class CannedRepliesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public CannedRepliesController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpGet]
    public Task<IReadOnlyList<CannedReplyDto>> List([FromQuery] string? search) =>
        _sender.Send(new ListCannedRepliesQuery(_currentUser.AgentUserId!.Value, search));

    public record SaveRequest(Guid? Id, string Shortcut, string Title, string Body, Guid? TeamId, bool Personal);

    [HttpPost]
    public Task<CannedReplyDto> Save(SaveRequest request) =>
        _sender.Send(new SaveCannedReplyCommand(
            request.Id, _currentUser.AgentUserId!.Value, request.Shortcut, request.Title,
            request.Body, request.TeamId, request.Personal));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _sender.Send(new DeleteCannedReplyCommand(id, _currentUser.AgentUserId!.Value, _currentUser.IsAdmin));
        return NoContent();
    }

    /// <summary>Renders a reply's placeholders against a conversation before inserting it in the composer.</summary>
    [HttpGet("{id:guid}/render")]
    public async Task<ActionResult> Render(Guid id, [FromQuery] Guid conversationId)
    {
        var body = await _sender.Send(new RenderCannedReplyQuery(id, conversationId, _currentUser.AgentUserId!.Value));
        return Ok(new { body });
    }
}
