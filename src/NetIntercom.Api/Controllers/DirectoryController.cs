using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NetIntercom.Application.Dtos;
using NetIntercom.Application.Teams;

namespace NetIntercom.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}")]
[Authorize(Policy = "Agent")]
public class DirectoryController : ControllerBase
{
    private readonly ISender _sender;

    public DirectoryController(ISender sender) => _sender = sender;

    [HttpGet("teams")]
    public Task<IReadOnlyList<TeamDto>> Teams() => _sender.Send(new GetTeamsQuery());

    [HttpGet("agents")]
    public Task<IReadOnlyList<AgentDto>> Agents() => _sender.Send(new GetAgentsQuery());

    [HttpGet("sla-policies")]
    public Task<IReadOnlyList<SlaPolicyDto>> SlaPolicies() => _sender.Send(new GetSlaPoliciesQuery());

    [HttpPost("sla-policies")]
    [Authorize(Policy = "Admin")]
    public Task<SlaPolicyDto> SaveSlaPolicy(SaveSlaPolicyCommand command) => _sender.Send(command);

    [HttpGet("routing-rules")]
    public Task<IReadOnlyList<RoutingRuleDto>> RoutingRules() => _sender.Send(new GetRoutingRulesQuery());

    [HttpPost("routing-rules")]
    [Authorize(Policy = "Admin")]
    public Task<RoutingRuleDto> SaveRoutingRule(SaveRoutingRuleCommand command) => _sender.Send(command);
}
