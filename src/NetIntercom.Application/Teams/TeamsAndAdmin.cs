using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Teams;

public record GetTeamsQuery : IRequest<IReadOnlyList<TeamDto>>;

public class GetTeamsHandler : IRequestHandler<GetTeamsQuery, IReadOnlyList<TeamDto>>
{
    private readonly IAppDbContext _db;
    private readonly IPresenceService _presence;

    public GetTeamsHandler(IAppDbContext db, IPresenceService presence)
    {
        _db = db;
        _presence = presence;
    }

    public async Task<IReadOnlyList<TeamDto>> Handle(GetTeamsQuery request, CancellationToken ct)
    {
        var teams = await _db.Teams
            .Include(t => t.Members).ThenInclude(m => m.AgentUser)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return teams.Select(t => TeamDto.From(t, _presence.GetStatus)).ToList();
    }
}

public record GetAgentsQuery : IRequest<IReadOnlyList<AgentDto>>;

public class GetAgentsHandler : IRequestHandler<GetAgentsQuery, IReadOnlyList<AgentDto>>
{
    private readonly IAppDbContext _db;
    private readonly IPresenceService _presence;

    public GetAgentsHandler(IAppDbContext db, IPresenceService presence)
    {
        _db = db;
        _presence = presence;
    }

    public async Task<IReadOnlyList<AgentDto>> Handle(GetAgentsQuery request, CancellationToken ct)
    {
        var agents = await _db.AgentUsers.Where(a => a.IsActive).OrderBy(a => a.DisplayName).ToListAsync(ct);
        return agents.Select(a => AgentDto.From(a, _presence.GetStatus(a.Id))).ToList();
    }
}

public record GetSlaPoliciesQuery : IRequest<IReadOnlyList<SlaPolicyDto>>;

public class GetSlaPoliciesHandler : IRequestHandler<GetSlaPoliciesQuery, IReadOnlyList<SlaPolicyDto>>
{
    private readonly IAppDbContext _db;

    public GetSlaPoliciesHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SlaPolicyDto>> Handle(GetSlaPoliciesQuery request, CancellationToken ct) =>
        await _db.SlaPolicies.OrderBy(p => p.Name).Select(p => SlaPolicyDto.From(p)).ToListAsync(ct);
}

public record SaveSlaPolicyCommand(Guid? Id, string Name, int FirstResponseMinutes, int NextResponseMinutes) : IRequest<SlaPolicyDto>;

public class SaveSlaPolicyValidator : AbstractValidator<SaveSlaPolicyCommand>
{
    public SaveSlaPolicyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.FirstResponseMinutes).InclusiveBetween(1, 60 * 24 * 7);
        RuleFor(x => x.NextResponseMinutes).InclusiveBetween(1, 60 * 24 * 7);
    }
}

public class SaveSlaPolicyHandler : IRequestHandler<SaveSlaPolicyCommand, SlaPolicyDto>
{
    private readonly IAppDbContext _db;

    public SaveSlaPolicyHandler(IAppDbContext db) => _db = db;

    public async Task<SlaPolicyDto> Handle(SaveSlaPolicyCommand request, CancellationToken ct)
    {
        SlaPolicy policy;
        if (request.Id is not null)
        {
            policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == request.Id, ct)
                     ?? throw new NotFoundException(nameof(SlaPolicy), request.Id);
        }
        else
        {
            policy = new SlaPolicy();
            _db.SlaPolicies.Add(policy);
        }

        policy.Name = request.Name;
        policy.FirstResponseMinutes = request.FirstResponseMinutes;
        policy.NextResponseMinutes = request.NextResponseMinutes;
        await _db.SaveChangesAsync(ct);
        return SlaPolicyDto.From(policy);
    }
}

public record GetRoutingRulesQuery : IRequest<IReadOnlyList<RoutingRuleDto>>;

public class GetRoutingRulesHandler : IRequestHandler<GetRoutingRulesQuery, IReadOnlyList<RoutingRuleDto>>
{
    private readonly IAppDbContext _db;

    public GetRoutingRulesHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoutingRuleDto>> Handle(GetRoutingRulesQuery request, CancellationToken ct) =>
        await _db.RoutingRules
            .Include(r => r.Team)
            .OrderBy(r => r.Priority)
            .Select(r => RoutingRuleDto.From(r))
            .ToListAsync(ct);
}

public record SaveRoutingRuleCommand(Guid? Id, string Name, int Priority, string? Keyword, bool IsEnabled, Guid TeamId) : IRequest<RoutingRuleDto>;

public class SaveRoutingRuleValidator : AbstractValidator<SaveRoutingRuleCommand>
{
    public SaveRoutingRuleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TeamId).NotEmpty();
    }
}

public class SaveRoutingRuleHandler : IRequestHandler<SaveRoutingRuleCommand, RoutingRuleDto>
{
    private readonly IAppDbContext _db;

    public SaveRoutingRuleHandler(IAppDbContext db) => _db = db;

    public async Task<RoutingRuleDto> Handle(SaveRoutingRuleCommand request, CancellationToken ct)
    {
        var teamExists = await _db.Teams.AnyAsync(t => t.Id == request.TeamId, ct);
        if (!teamExists)
        {
            throw new NotFoundException(nameof(Team), request.TeamId);
        }

        RoutingRule rule;
        if (request.Id is not null)
        {
            rule = await _db.RoutingRules.Include(r => r.Team).FirstOrDefaultAsync(r => r.Id == request.Id, ct)
                   ?? throw new NotFoundException(nameof(RoutingRule), request.Id);
        }
        else
        {
            rule = new RoutingRule();
            _db.RoutingRules.Add(rule);
        }

        rule.Name = request.Name;
        rule.Priority = request.Priority;
        rule.Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        rule.IsEnabled = request.IsEnabled;
        rule.TeamId = request.TeamId;
        await _db.SaveChangesAsync(ct);

        rule.Team = await _db.Teams.FirstAsync(t => t.Id == rule.TeamId, ct);
        return RoutingRuleDto.From(rule);
    }
}
