using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Application.Widget;

namespace NetIntercom.Api.Controllers;

/// <summary>
/// The widget's minimal API surface. The session endpoint is anonymous (cookie identity);
/// everything else requires the visitor token it returns. Rate limited per client IP.
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/widget")]
[EnableCors("widget")]
[EnableRateLimiting("widget")]
public class WidgetController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public WidgetController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpPost("session")]
    [AllowAnonymous]
    public Task<WidgetSession> StartSession(StartWidgetSessionCommand command) => _sender.Send(command);

    public record SendRequest(Guid? ConversationId, string Body, string? SourceUrl, bool StartNew = false);

    [HttpPost("messages")]
    [Authorize(Policy = "Visitor")]
    public async Task<ActionResult> Send(SendRequest request)
    {
        var (conversation, message) = await _sender.Send(new SendVisitorMessageCommand(
            _currentUser.VisitorId!.Value, request.ConversationId, request.Body, request.SourceUrl, request.StartNew));
        return Ok(new { conversation, message });
    }

    /// <summary>Message history / reconnect catch-up for the visitor's own conversation.</summary>
    [HttpGet("conversations/{id:guid}/messages")]
    [Authorize(Policy = "Visitor")]
    public Task<IReadOnlyList<MessageDto>> Messages(Guid id, [FromQuery] DateTime? after) =>
        _sender.Send(new GetVisitorMessagesQuery(_currentUser.VisitorId!.Value, id, after));

    [HttpPost("conversations/{id:guid}/read")]
    [Authorize(Policy = "Visitor")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        await _sender.Send(new MarkReadByVisitorCommand(_currentUser.VisitorId!.Value, id));
        return NoContent();
    }
}
