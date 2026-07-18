using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NetIntercom.Application.Common;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Infrastructure.Auth;

public class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "netintercom";
    public string Audience { get; set; } = "netintercom";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int VisitorTokenDays { get; set; } = 30;
}

public static class AppClaims
{
    public const string Role = ClaimTypes.Role;
    public const string AgentRole = "agent";
    public const string AdminRole = "admin";
    public const string VisitorRole = "visitor";
    public const string VisitorId = "visitor_id";
}

public class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public TokenService(JwtOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public AgentTokenPair CreateAgentTokens(AgentUser agent)
    {
        var accessExpires = _clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, agent.Email),
            new(JwtRegisteredClaimNames.Name, agent.DisplayName),
            new(AppClaims.Role, AppClaims.AgentRole)
        };
        if (agent.IsAdmin)
        {
            claims.Add(new Claim(AppClaims.Role, AppClaims.AdminRole));
        }

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return new AgentTokenPair(
            WriteToken(claims, accessExpires),
            accessExpires,
            refreshToken,
            _clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    public string CreateVisitorToken(Visitor visitor)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, visitor.Id.ToString()),
            new(AppClaims.VisitorId, visitor.Id.ToString()),
            new(AppClaims.Role, AppClaims.VisitorRole)
        };
        return WriteToken(claims, _clock.UtcNow.AddDays(_options.VisitorTokenDays));
    }

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private string WriteToken(IEnumerable<Claim> claims, DateTime expiresUtc)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: _clock.UtcNow,
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
