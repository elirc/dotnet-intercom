using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Auth;

/// <summary>Rotates the refresh token: the presented token is revoked and a new pair is issued.</summary>
public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResult>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public RefreshTokenCommandHandler(IAppDbContext db, ITokenService tokens, IClock clock)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<AuthResult> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var hash = _tokens.HashRefreshToken(request.RefreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.AgentUser)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive(_clock.UtcNow) || !stored.AgentUser.IsActive)
        {
            throw new UnauthorizedException("Refresh token is invalid or expired.");
        }

        stored.RevokedAtUtc = _clock.UtcNow;

        var pair = _tokens.CreateAgentTokens(stored.AgentUser);
        _db.RefreshTokens.Add(new RefreshToken
        {
            AgentUserId = stored.AgentUserId,
            TokenHash = _tokens.HashRefreshToken(pair.RefreshToken),
            CreatedAtUtc = _clock.UtcNow,
            ExpiresAtUtc = pair.RefreshTokenExpiresAtUtc
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResult(pair.AccessToken, pair.AccessTokenExpiresAtUtc, pair.RefreshToken, AgentDto.From(stored.AgentUser));
    }
}
