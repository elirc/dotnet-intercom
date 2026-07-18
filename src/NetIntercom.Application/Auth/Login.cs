using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Auth;

public record AuthResult(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, AgentDto Agent);

public record LoginCommand(string Email, string Password) : IRequest<AuthResult>;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordService _passwords;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public LoginCommandHandler(IAppDbContext db, IPasswordService passwords, ITokenService tokens, IClock clock)
    {
        _db = db;
        _passwords = passwords;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var agent = await _db.AgentUsers
            .FirstOrDefaultAsync(a => a.Email == request.Email.ToLowerInvariant() && a.IsActive, ct);

        if (agent is null || !_passwords.Verify(agent.PasswordHash, request.Password))
        {
            throw new UnauthorizedException();
        }

        var pair = _tokens.CreateAgentTokens(agent);
        _db.RefreshTokens.Add(new RefreshToken
        {
            AgentUserId = agent.Id,
            TokenHash = _tokens.HashRefreshToken(pair.RefreshToken),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = pair.RefreshTokenExpiresAtUtc
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResult(pair.AccessToken, pair.AccessTokenExpiresAtUtc, pair.RefreshToken, AgentDto.From(agent));
    }
}
