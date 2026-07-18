using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NetIntercom.Application.Auth;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Application.Teams;

namespace NetIntercom.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUser _currentUser;

    public AuthController(ISender sender, ICurrentUser currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public Task<AuthResult> Login(LoginCommand command) => _sender.Send(command);

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public Task<AuthResult> Refresh(RefreshTokenCommand command) => _sender.Send(command);

    [HttpGet("me")]
    [Authorize(Policy = "Agent")]
    public async Task<ActionResult<AgentDto>> Me()
    {
        var agents = await _sender.Send(new GetAgentsQuery());
        var me = agents.FirstOrDefault(a => a.Id == _currentUser.AgentUserId);
        return me is null ? NotFound() : Ok(me);
    }
}
