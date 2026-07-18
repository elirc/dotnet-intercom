using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Application.Common;

public interface IAppDbContext
{
    DbSet<Visitor> Visitors { get; }
    DbSet<AgentUser> AgentUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<SlaPolicy> SlaPolicies { get; }
    DbSet<SlaTimer> SlaTimers { get; }
    DbSet<RoutingRule> RoutingRules { get; }
    DbSet<CannedReply> CannedReplies { get; }
    DbSet<PresenceSnapshot> PresenceSnapshots { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Identity of the caller, populated from JWT claims by the API layer.</summary>
public interface ICurrentUser
{
    Guid? AgentUserId { get; }
    Guid? VisitorId { get; }
    bool IsAdmin { get; }
}

/// <summary>Pushes events to connected SignalR clients. Implemented in the API project; the DB stays source-of-truth.</summary>
public interface IRealtimeNotifier
{
    /// <summary>Delivers a new message to the conversation group(s). Internal notes never reach the visitor.</summary>
    Task MessageCreatedAsync(MessageDto message, CancellationToken ct = default);

    /// <summary>Refreshes inbox lists (state/assignment/last-message changes).</summary>
    Task ConversationUpdatedAsync(ConversationDto conversation, CancellationToken ct = default);

    Task SlaBreachedAsync(ConversationDto conversation, SlaTimerKind kind, CancellationToken ct = default);

    Task MessagesReadAsync(Guid conversationId, SenderType readBy, DateTime readAtUtc, CancellationToken ct = default);
}

/// <summary>Live agent presence derived from hub connections + heartbeats (implemented in the API project).</summary>
public interface IPresenceService
{
    IReadOnlyCollection<Guid> GetOnlineAgentIds();
    PresenceStatus GetStatus(Guid agentUserId);
}

public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public record AgentTokenPair(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken, DateTime RefreshTokenExpiresAtUtc);

public interface ITokenService
{
    AgentTokenPair CreateAgentTokens(AgentUser agent);
    string CreateVisitorToken(Visitor visitor);
    string HashRefreshToken(string refreshToken);
}
