using FluentAssertions;
using NetIntercom.Domain;
using NetIntercom.Domain.Common;
using NetIntercom.Domain.Entities;
using Xunit;

namespace NetIntercom.UnitTests;

public class ConversationTests
{
    private static readonly DateTime Now = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);

    private static Conversation NewConversation(SlaPolicy? policy = null)
    {
        var conversation = new Conversation { VisitorId = Guid.NewGuid() };
        if (policy is not null)
        {
            conversation.SlaPolicy = policy;
            conversation.SlaPolicyId = policy.Id;
        }

        return conversation;
    }

    private static SlaPolicy Policy => new() { FirstResponseMinutes = 15, NextResponseMinutes = 30 };

    [Fact]
    public void VisitorMessage_StartsFirstResponseTimer()
    {
        var conversation = NewConversation(Policy);

        conversation.RecordVisitorMessage(Now);

        var timer = conversation.SlaTimers.Single();
        timer.Kind.Should().Be(SlaTimerKind.FirstResponse);
        timer.Status.Should().Be(SlaTimerStatus.Running);
        timer.BreachAtUtc.Should().Be(Now.AddMinutes(15));
    }

    [Fact]
    public void VisitorMessage_WithoutSlaPolicy_StartsNoTimer()
    {
        var conversation = NewConversation();
        conversation.RecordVisitorMessage(Now);
        conversation.SlaTimers.Should().BeEmpty();
    }

    [Fact]
    public void VisitorMessage_WithRunningTimer_DoesNotStack()
    {
        var conversation = NewConversation(Policy);
        conversation.RecordVisitorMessage(Now);
        conversation.RecordVisitorMessage(Now.AddMinutes(1));
        conversation.SlaTimers.Should().HaveCount(1);
    }

    [Fact]
    public void AgentReply_MeetsTimer_AndStampsFirstResponse()
    {
        var conversation = NewConversation(Policy);
        conversation.RecordVisitorMessage(Now);

        conversation.RecordAgentReply(Now.AddMinutes(5));

        conversation.FirstAgentReplyAtUtc.Should().Be(Now.AddMinutes(5));
        conversation.SlaTimers.Single().Status.Should().Be(SlaTimerStatus.Met);
    }

    [Fact]
    public void VisitorMessage_AfterFirstReply_StartsNextResponseTimer()
    {
        var conversation = NewConversation(Policy);
        conversation.RecordVisitorMessage(Now);
        conversation.RecordAgentReply(Now.AddMinutes(5));

        conversation.RecordVisitorMessage(Now.AddMinutes(10));

        var next = conversation.SlaTimers.Single(t => t.Status == SlaTimerStatus.Running);
        next.Kind.Should().Be(SlaTimerKind.NextResponse);
        next.BreachAtUtc.Should().Be(Now.AddMinutes(10 + 30));
    }

    [Fact]
    public void Close_CancelsRunningTimers_AndCloseTwiceThrows()
    {
        var conversation = NewConversation(Policy);
        conversation.RecordVisitorMessage(Now);

        conversation.Close(Now.AddMinutes(1));

        conversation.State.Should().Be(ConversationState.Closed);
        conversation.SlaTimers.Single().Status.Should().Be(SlaTimerStatus.Cancelled);
        var act = () => conversation.Close(Now.AddMinutes(2));
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Snooze_InThePast_Throws()
    {
        var conversation = NewConversation();
        var act = () => conversation.Snooze(Now.AddMinutes(-1), Now);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void VisitorMessage_OnClosedConversation_Reopens()
    {
        var conversation = NewConversation();
        conversation.Close(Now);
        conversation.RecordVisitorMessage(Now.AddMinutes(1));
        conversation.State.Should().Be(ConversationState.Open);
    }

    [Fact]
    public void Assign_OnClosedConversation_Throws()
    {
        var conversation = NewConversation();
        conversation.Close(Now);
        var act = () => conversation.Assign(Guid.NewGuid(), null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SlaTimer_Breach_OnlyFromRunning()
    {
        var timer = SlaTimer.Start(Guid.NewGuid(), SlaTimerKind.FirstResponse, Now, TimeSpan.FromMinutes(15));
        timer.MarkBreached(Now.AddMinutes(16));
        timer.Status.Should().Be(SlaTimerStatus.Breached);

        var act = () => timer.MarkMet(Now.AddMinutes(17));
        act.Should().Throw<DomainException>();
    }
}
