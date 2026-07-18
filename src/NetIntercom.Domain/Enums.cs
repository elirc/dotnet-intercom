namespace NetIntercom.Domain;

public enum ConversationState
{
    Open = 0,
    Snoozed = 1,
    Closed = 2
}

public enum SenderType
{
    Visitor = 0,
    Agent = 1,
    System = 2
}

public enum SlaTimerKind
{
    FirstResponse = 0,
    NextResponse = 1
}

public enum SlaTimerStatus
{
    Running = 0,
    Met = 1,
    Breached = 2,
    Cancelled = 3
}

public enum PresenceStatus
{
    Offline = 0,
    Away = 1,
    Online = 2
}
