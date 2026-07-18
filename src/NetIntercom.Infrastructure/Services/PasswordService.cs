using Microsoft.AspNetCore.Identity;
using NetIntercom.Application.Common;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Infrastructure.Services;

/// <summary>Wraps ASP.NET Core Identity's PBKDF2 hasher without taking on full Identity.</summary>
public class PasswordService : IPasswordService
{
    private static readonly PasswordHasher<AgentUser> Hasher = new();
    private static readonly AgentUser Dummy = new();

    public string Hash(string password) => Hasher.HashPassword(Dummy, password);

    public bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(Dummy, hash, password) != PasswordVerificationResult.Failed;
}
