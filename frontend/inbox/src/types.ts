export type ConversationState = "Open" | "Snoozed" | "Closed";
export type SenderType = "Visitor" | "Agent" | "System";
export type PresenceStatus = "Offline" | "Away" | "Online";

export interface AgentDto {
  id: string;
  email: string;
  displayName: string;
  isAdmin: boolean;
  presence: PresenceStatus;
}

export interface VisitorDto {
  id: string;
  anonymousId: string;
  name: string | null;
  email: string | null;
  displayName: string;
  lastSeenAtUtc: string;
  attributesJson: string | null;
}

export interface SlaTimerDto {
  id: string;
  kind: "FirstResponse" | "NextResponse";
  status: "Running" | "Met" | "Breached" | "Cancelled";
  startedAtUtc: string;
  breachAtUtc: string;
  completedAtUtc: string | null;
}

export interface ConversationDto {
  id: string;
  state: ConversationState;
  subject: string | null;
  sourceUrl: string | null;
  visitor: VisitorDto;
  teamId: string | null;
  teamName: string | null;
  assigneeId: string | null;
  assigneeName: string | null;
  createdAtUtc: string;
  lastMessageAtUtc: string;
  lastMessagePreview: string | null;
  firstAgentReplyAtUtc: string | null;
  closedAtUtc: string | null;
  snoozedUntilUtc: string | null;
  activeSlaTimer: SlaTimerDto | null;
  hasBreachedSla: boolean;
}

export interface MessageDto {
  id: string;
  conversationId: string;
  senderType: SenderType;
  agentUserId: string | null;
  senderName: string;
  body: string;
  isInternalNote: boolean;
  createdAtUtc: string;
  readByVisitorAtUtc: string | null;
  readByAgentAtUtc: string | null;
}

export interface TeamDto {
  id: string;
  name: string;
  isFallback: boolean;
  defaultSlaPolicyId: string | null;
  members: AgentDto[];
}

export interface CannedReplyDto {
  id: string;
  shortcut: string;
  title: string;
  body: string;
  teamId: string | null;
  agentUserId: string | null;
}

export interface Paginated<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AuthResult {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  agent: AgentDto;
}
