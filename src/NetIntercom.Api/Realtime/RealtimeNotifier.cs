using Microsoft.AspNetCore.SignalR;
using NetIntercom.Application.Common;
using NetIntercom.Application.Dtos;
using NetIntercom.Domain;

namespace NetIntercom.Api.Realtime;

/// <summary>SignalR fan-out for application events. Internal notes are never sent to visitors.</summary>
public class RealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<AgentHub> _agentHub;
    private readonly IHubContext<VisitorHub> _visitorHub;

    public RealtimeNotifier(IHubContext<AgentHub> agentHub, IHubContext<VisitorHub> visitorHub)
    {
        _agentHub = agentHub;
        _visitorHub = visitorHub;
    }

    public async Task MessageCreatedAsync(MessageDto message, CancellationToken ct = default)
    {
        await _agentHub.Clients.Group("agents").SendAsync("messageCreated", message, ct);
        if (!message.IsInternalNote)
        {
            await _visitorHub.Clients.Group($"vconv:{message.ConversationId}").SendAsync("messageCreated", message, ct);
        }
    }

    public async Task ConversationUpdatedAsync(ConversationDto conversation, CancellationToken ct = default)
    {
        await _agentHub.Clients.Group("agents").SendAsync("conversationUpdated", conversation, ct);
        await _visitorHub.Clients.Group($"vconv:{conversation.Id}").SendAsync("conversationUpdated", new
        {
            conversation.Id,
            conversation.State,
            assigneeName = conversation.AssigneeName
        }, ct);
    }

    public Task SlaBreachedAsync(ConversationDto conversation, SlaTimerKind kind, CancellationToken ct = default) =>
        _agentHub.Clients.Group("agents").SendAsync("slaBreached", new { conversation, kind = kind.ToString() }, ct);

    public async Task MessagesReadAsync(Guid conversationId, SenderType readBy, DateTime readAtUtc, CancellationToken ct = default)
    {
        var payload = new { conversationId, readBy = readBy.ToString(), readAtUtc };
        await _agentHub.Clients.Group("agents").SendAsync("messagesRead", payload, ct);
        await _visitorHub.Clients.Group($"vconv:{conversationId}").SendAsync("messagesRead", payload, ct);
    }
}
