using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

public class Team : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The fallback team receives conversations no routing rule claims (or outside office hours).</summary>
    public bool IsFallback { get; set; }

    /// <summary>Office hours definition serialized as JSON (see <see cref="OfficeHours"/>). Null = always open.</summary>
    public string? OfficeHoursJson { get; set; }

    public Guid? DefaultSlaPolicyId { get; set; }
    public SlaPolicy? DefaultSlaPolicy { get; set; }

    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
}

public class TeamMember : Entity
{
    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
    public Guid AgentUserId { get; set; }
    public AgentUser AgentUser { get; set; } = null!;

    /// <summary>Round-robin cursor: the member assigned least recently gets the next conversation.</summary>
    public DateTime? LastAssignedAtUtc { get; set; }
}
