using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;
using NetIntercom.Domain.Common;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Infrastructure.Persistence;

public class AppDbContext : DbContext, IAppDbContext
{
    private readonly IClock _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, IClock clock) : base(options)
    {
        _clock = clock;
    }

    public DbSet<Visitor> Visitors => Set<Visitor>();
    public DbSet<AgentUser> AgentUsers => Set<AgentUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<SlaTimer> SlaTimers => Set<SlaTimer>();
    public DbSet<RoutingRule> RoutingRules => Set<RoutingRule>();
    public DbSet<CannedReply> CannedReplies => Set<CannedReply>();
    public DbSet<PresenceSnapshot> PresenceSnapshots => Set<PresenceSnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Visitor>(e =>
        {
            e.HasIndex(v => v.AnonymousId).IsUnique();
            e.HasIndex(v => v.ExternalId);
            e.Property(v => v.AnonymousId).HasMaxLength(64);
            e.Property(v => v.Email).HasMaxLength(320);
            e.Property(v => v.Name).HasMaxLength(200);
            e.Property(v => v.AttributesJson).HasColumnType("jsonb");
            e.Ignore(v => v.DisplayName);
        });

        b.Entity<AgentUser>(e =>
        {
            e.HasIndex(a => a.Email).IsUnique();
            e.Property(a => a.Email).HasMaxLength(320);
            e.Property(a => a.DisplayName).HasMaxLength(200);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.Property(t => t.TokenHash).HasMaxLength(128);
            e.HasOne(t => t.AgentUser).WithMany(a => a.RefreshTokens).HasForeignKey(t => t.AgentUserId);
        });

        b.Entity<Team>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(100);
            e.HasOne(t => t.DefaultSlaPolicy).WithMany().HasForeignKey(t => t.DefaultSlaPolicyId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TeamMember>(e =>
        {
            e.HasIndex(m => new { m.TeamId, m.AgentUserId }).IsUnique();
            e.HasOne(m => m.Team).WithMany(t => t.Members).HasForeignKey(m => m.TeamId);
            e.HasOne(m => m.AgentUser).WithMany(a => a.TeamMemberships).HasForeignKey(m => m.AgentUserId);
        });

        b.Entity<Conversation>(e =>
        {
            e.HasIndex(c => new { c.State, c.LastMessageAtUtc });
            e.HasIndex(c => c.AssigneeId);
            e.HasIndex(c => c.TeamId);
            e.Property(c => c.Subject).HasMaxLength(200);
            e.Property(c => c.SourceUrl).HasMaxLength(2000);
            e.Property(c => c.LastMessagePreview).HasMaxLength(200);
            // PostgreSQL xmin system column as optimistic concurrency token.
            e.Property(c => c.RowVersion).IsRowVersion();
            e.HasOne(c => c.Visitor).WithMany(v => v.Conversations).HasForeignKey(c => c.VisitorId);
            e.HasOne(c => c.Team).WithMany().HasForeignKey(c => c.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.Assignee).WithMany().HasForeignKey(c => c.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(c => c.SlaPolicy).WithMany().HasForeignKey(c => c.SlaPolicyId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Message>(e =>
        {
            e.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });
            e.HasOne(m => m.Conversation).WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId);
            e.HasOne(m => m.AgentUser).WithMany().HasForeignKey(m => m.AgentUserId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<SlaPolicy>(e => e.Property(p => p.Name).HasMaxLength(100));

        b.Entity<SlaTimer>(e =>
        {
            e.HasIndex(t => new { t.Status, t.BreachAtUtc });
            e.HasOne(t => t.Conversation).WithMany(c => c.SlaTimers).HasForeignKey(t => t.ConversationId);
        });

        b.Entity<RoutingRule>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(100);
            e.Property(r => r.Keyword).HasMaxLength(200);
            e.HasOne(r => r.Team).WithMany().HasForeignKey(r => r.TeamId);
        });

        b.Entity<CannedReply>(e =>
        {
            e.Property(r => r.Shortcut).HasMaxLength(50);
            e.Property(r => r.Title).HasMaxLength(200);
            e.HasIndex(r => r.Shortcut);
            e.HasQueryFilter(r => !r.IsDeleted);
        });

        b.Entity<PresenceSnapshot>(e =>
        {
            e.HasIndex(p => p.AgentUserId).IsUnique();
            e.HasOne(p => p.AgentUser).WithMany().HasForeignKey(p => p.AgentUserId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity auditable)
            {
                if (entry.State == EntityState.Added && auditable.CreatedAtUtc == default)
                {
                    auditable.CreatedAtUtc = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAtUtc = now;
                }
            }

            if (entry is { State: EntityState.Deleted, Entity: ISoftDeletable soft })
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAtUtc = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
