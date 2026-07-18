export interface MessageDto {
  id: string;
  conversationId: string;
  senderType: "Visitor" | "Agent" | "System";
  senderName: string;
  body: string;
  isInternalNote: boolean;
  createdAtUtc: string;
  readByVisitorAtUtc: string | null;
}

export interface WidgetConversation {
  id: string;
  state: "Open" | "Snoozed" | "Closed";
  assigneeName: string | null;
}

export interface WidgetSession {
  visitorToken: string;
  visitor: { id: string; anonymousId: string; name: string | null };
  conversation: WidgetConversation | null;
  messages: MessageDto[];
}

const COOKIE = "netintercom_vid";

/** Cookie-based anonymous identity, stable across visits. */
export function getAnonymousId(): string {
  const existing = document.cookie
    .split("; ")
    .find((c) => c.startsWith(`${COOKIE}=`))
    ?.split("=")[1];
  if (existing) return existing;
  const id = `anon-${crypto.randomUUID().replace(/-/g, "")}`;
  document.cookie = `${COOKIE}=${id}; path=/; max-age=${60 * 60 * 24 * 365}; SameSite=Lax`;
  return id;
}

export class WidgetApi {
  constructor(
    private base: string,
    private token: string | null = null,
  ) {}

  setToken(token: string) {
    this.token = token;
  }

  async startSession(user?: { externalId: string; name?: string; email?: string }, attributes?: object) {
    const session = await this.post<WidgetSession>("/api/v1/widget/session", {
      anonymousId: getAnonymousId(),
      name: user?.name,
      email: user?.email,
      externalId: user?.externalId,
      attributesJson: attributes ? JSON.stringify(attributes) : null,
    });
    this.token = session.visitorToken;
    return session;
  }

  sendMessage(body: string, conversationId: string | null) {
    return this.post<{ conversation: WidgetConversation; message: MessageDto }>("/api/v1/widget/messages", {
      conversationId,
      body,
      sourceUrl: window.location.href,
    });
  }

  /** Reconnect catch-up: server is source-of-truth for the transcript. */
  getMessages(conversationId: string, afterUtc?: string) {
    const query = afterUtc ? `?after=${encodeURIComponent(afterUtc)}` : "";
    return this.get<MessageDto[]>(`/api/v1/widget/conversations/${conversationId}/messages${query}`);
  }

  markRead(conversationId: string) {
    return this.post(`/api/v1/widget/conversations/${conversationId}/read`, {});
  }

  get token_() {
    return this.token;
  }

  private headers() {
    return {
      "Content-Type": "application/json",
      ...(this.token ? { Authorization: `Bearer ${this.token}` } : {}),
    };
  }

  private async get<T>(path: string): Promise<T> {
    const r = await fetch(`${this.base}${path}`, { headers: this.headers() });
    if (!r.ok) throw new Error(`Request failed (${r.status})`);
    return r.json();
  }

  private async post<T>(path: string, body: unknown): Promise<T> {
    const r = await fetch(`${this.base}${path}`, {
      method: "POST",
      headers: this.headers(),
      body: JSON.stringify(body),
    });
    if (!r.ok) throw new Error(`Request failed (${r.status})`);
    return r.status === 204 ? (undefined as T) : r.json();
  }
}
