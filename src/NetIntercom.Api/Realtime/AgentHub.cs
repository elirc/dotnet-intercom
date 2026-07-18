using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NetIntercom.Api.Realtime;

/// <summary>
/// Authenticated agent connection. Delivery only — messages are written through the REST API
/// and the DB stays source-of-truth; reconnect catch-up uses GET messages?after=…
/// Groups: "agents" (everyone, inbox updates) and "conv:{id}" (agents viewing a conversation).
/// </summary>
[Authorize(Policy = "Agent")]
public class AgentHub : Hub
{
    private readonly PresenceTracker _presence;
    private readonly IHubContext<VisitorHub> _visitorHub;

    public AgentHub(PresenceTracker presence, IHubContext<VisitorHub> visitorHub)
    {
        _presence = presence;
        _visitorHub = visitorHub;
    }

    private Guid AgentId => Guid.Parse(Context.UserIdentifier!);
    private string AgentName => Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Agent";

    public override async Task OnConnectedAsync()
    {
        _presence.Connected(AgentId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, "agents");
        await Clients.Group("agents").SendAsync("presenceChanged", new { agentUserId = AgentId, status = "Online" });
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Presence transitions (with grace periods) are broadcast by the sweeper, not here.
        var viewing = _presence.RemoveAllViewersForConnection(Context.ConnectionId);
        _presence.Disconnected(AgentId, Context.ConnectionId);
        foreach (var conversationId in viewing)
        {
            await Clients.OthersInGroup("agents").SendAsync("viewerLeft",
                new { conversationId, agentUserId = AgentId, agentName = AgentName });
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Marks this agent as actively viewing a conversation; returns current viewers for collision UI.</summary>
    public async Task<object> JoinConversation(Guid conversationId)
    {
        await Heartbeat();
        await Groups.AddToGroupAsync(Context.ConnectionId, $"conv:{conversationId}");
        var viewers = _presence.AddViewer(conversationId, Context.ConnectionId, AgentId, AgentName);
        await Clients.OthersInGroup("agents").SendAsync("viewerJoined",
            new { conversationId, agentUserId = AgentId, agentName = AgentName });
        return viewers.Select(v => new { agentUserId = v.AgentId, agentName = v.Name }).ToList();
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"conv:{conversationId}");
        _presence.RemoveViewer(conversationId, Context.ConnectionId);
        await Clients.OthersInGroup("agents").SendAsync("viewerLeft",
            new { conversationId, agentUserId = AgentId, agentName = AgentName });
    }

    /// <summary>Typing indicator fan-out: other agents viewing the conversation + the visitor (cross-hub).</summary>
    public async Task Typing(Guid conversationId, bool isTyping)
    {
        await Heartbeat();
        var payload = new { conversationId, source = "agent", name = AgentName, isTyping };
        await Clients.OthersInGroup($"conv:{conversationId}").SendAsync("typing", payload);
        await _visitorHub.Clients.Group($"vconv:{conversationId}").SendAsync("typing", payload);
    }

    /// <summary>Activity heartbeat from the client (keypress/visibility); feeds Away detection.</summary>
    public Task Heartbeat()
    {
        _presence.Heartbeat(AgentId);
        return Task.CompletedTask;
    }
}
