using NetIntercom.Domain.Common;

namespace NetIntercom.Domain.Entities;

/// <summary>
/// Rules are evaluated in ascending <see cref="Priority"/> order. A rule matches when
/// <see cref="Keyword"/> is null (catch-all) or appears in the first message / source URL.
/// The matched team's online members are assigned round-robin; outside office hours the
/// conversation falls through to the fallback team's queue.
/// </summary>
public class RoutingRule : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string? Keyword { get; set; }
    public bool IsEnabled { get; set; } = true;

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;

    public bool Matches(string firstMessage, string? sourceUrl)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Keyword))
        {
            return true;
        }

        return firstMessage.Contains(Keyword, StringComparison.OrdinalIgnoreCase)
               || (sourceUrl?.Contains(Keyword, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
