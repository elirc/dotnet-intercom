using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetIntercom.Application.Common;
using NetIntercom.Domain.Entities;

namespace NetIntercom.Infrastructure.Persistence;

/// <summary>Idempotent development seed: teams, agents, SLA policies, routing rules, canned replies.</summary>
public static class DbSeeder
{
    public const string DefaultPassword = "Passw0rd!";

    public static async Task SeedAsync(AppDbContext db, IPasswordService passwords, ILogger logger, CancellationToken ct = default)
    {
        if (await db.AgentUsers.AnyAsync(ct))
        {
            return;
        }

        logger.LogInformation("Seeding development data");

        var slaStandard = new SlaPolicy { Name = "Standard (15m/30m)", FirstResponseMinutes = 15, NextResponseMinutes = 30 };
        var slaRelaxed = new SlaPolicy { Name = "Relaxed (60m/120m)", FirstResponseMinutes = 60, NextResponseMinutes = 120 };
        db.SlaPolicies.AddRange(slaStandard, slaRelaxed);

        var businessHours = new OfficeHours
        {
            TimeZoneId = "UTC",
            Windows = Enum.GetValues<DayOfWeek>()
                .Where(d => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                .Select(d => new OfficeHoursWindow { Day = d, Start = new TimeOnly(8, 0), End = new TimeOnly(18, 0) })
                .ToList()
        };

        var support = new Team { Name = "Support", IsFallback = true, DefaultSlaPolicy = slaStandard };
        var billing = new Team { Name = "Billing", DefaultSlaPolicy = slaRelaxed, OfficeHoursJson = businessHours.ToJson() };
        db.Teams.AddRange(support, billing);

        var admin = NewAgent("admin@netintercom.local", "Avery Admin", passwords, isAdmin: true);
        var sara = NewAgent("sara@netintercom.local", "Sara Support", passwords);
        var alex = NewAgent("alex@netintercom.local", "Alex Billing", passwords);
        db.AgentUsers.AddRange(admin, sara, alex);

        db.TeamMembers.AddRange(
            new TeamMember { Team = support, AgentUser = admin },
            new TeamMember { Team = support, AgentUser = sara },
            new TeamMember { Team = billing, AgentUser = alex });

        db.RoutingRules.AddRange(
            new RoutingRule { Name = "Billing questions", Priority = 10, Keyword = "billing", Team = billing },
            new RoutingRule { Name = "Invoices", Priority = 11, Keyword = "invoice", Team = billing },
            new RoutingRule { Name = "Catch-all to Support", Priority = 100, Team = support });

        db.CannedReplies.AddRange(
            new CannedReply
            {
                Shortcut = "hello",
                Title = "Greeting",
                Body = "Hi {{visitor.name}}, thanks for reaching out! I'm {{agent.name}} from the {{team.name}} team — how can I help?",
                Team = support
            },
            new CannedReply
            {
                Shortcut = "refund",
                Title = "Refund policy",
                Body = "Hi {{visitor.name}}, refunds are processed within 5–7 business days once approved.",
                Team = billing
            },
            new CannedReply
            {
                Shortcut = "brb",
                Title = "Be right back",
                Body = "One moment please, {{visitor.name}} — checking that for you now.",
                AgentUser = sara
            });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed complete. Agent logins use password '{Password}'", DefaultPassword);
    }

    private static AgentUser NewAgent(string email, string name, IPasswordService passwords, bool isAdmin = false) => new()
    {
        Email = email,
        DisplayName = name,
        PasswordHash = passwords.Hash(DefaultPassword),
        IsAdmin = isAdmin
    };
}
