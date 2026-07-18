using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

public class Visitor : AuditableEntity
{
    /// <summary>Stable identifier stored in the widget cookie for anonymous visitors.</summary>
    public string AnonymousId { get; set; } = string.Empty;

    /// <summary>Set when the host site identifies the user (e.g. their own user id).</summary>
    public string? ExternalId { get; set; }

    public string? Name { get; set; }
    public string? Email { get; set; }
    public DateTime LastSeenAtUtc { get; set; }

    /// <summary>Arbitrary context supplied by the widget (page url, plan, etc.) as JSON.</summary>
    public string? AttributesJson { get; set; }

    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();

    public string DisplayName => Name ?? Email ?? $"Visitor {AnonymousId[..Math.Min(8, AnonymousId.Length)]}";
}
