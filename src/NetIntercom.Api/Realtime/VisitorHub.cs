using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NetIntercom.Application.Common;

namespace NetIntercom.Api.Realtime;

/// <summary>
/// Widget connection, authenticated with the visitor-scoped JWT issued by the widget session
/// endpoint (passed as access_token in the query string). Delivery only; after a reconnect the
/// widget calls GET /api/v1/widget/conversations/{id}/messages?after=… to catch up.
/// </summary>
[Authorize(Policy = "Visitor")]
public class VisitorHub : Hub
{
    private readonly IAppDbContext _db;
    private readonly IHubContext<AgentHub> _agentHub;
    private readonly PresenceTracker _presence;

    public VisitorHub(IAppDbContext db, IHubContext<AgentHub> agentHub, PresenceTracker presence)
    {
        _db = db;
        _agentHub = agentHub;
        _presence = presence;
    }

    private Guid VisitorId => Guid.Parse(Context.User!.FindFirst("visitor_id")!.Value);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "visitors");
        await Clients.Caller.SendAsync("teamPresence", new { anyAgentOnline = _presence.GetOnlineAgentIds().Count > 0 });
        await base.OnConnectedAsync();
    }

    /// <summary>Subscribes this widget to its conversation's events. Ownership is verified.</summary>
    public async Task JoinConversation(Guid conversationId)
    {
        var owns = await _db.Conversations.AnyAsync(c => c.Id == conversationId && c.VisitorId == VisitorId);
        if (!owns)
        {
            throw new HubException("Conversation does not belong to this visitor.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"vconv:{conversationId}");
    }

    public async Task Typing(Guid conversationId, bool isTyping)
    {
        var visitor = await _db.Visitors.FirstOrDefaultAsync(v => v.Id == VisitorId);
        await _agentHub.Clients.Group($"conv:{conversationId}").SendAsync("typing", new
        {
            conversationId,
            source = "visitor",
            name = visitor?.DisplayName ?? "Visitor",
            isTyping
        });
    }
}
