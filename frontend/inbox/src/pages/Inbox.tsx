import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, post, put } from "../api";
import { startConnection } from "../realtime";
import { useAuth } from "../auth";
import type {
  AgentDto,
  CannedReplyDto,
  ConversationDto,
  ConversationState,
  MessageDto,
  Paginated,
  TeamDto,
} from "../types";

type Filter = { state: ConversationState; mine: boolean };

export default function InboxPage() {
  const { agent } = useAuth();
  const queryClient = useQueryClient();
  const [filter, setFilter] = useState<Filter>({ state: "Open", mine: false });
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [viewers, setViewers] = useState<Record<string, { agentUserId: string; agentName: string }[]>>({});
  const [typing, setTyping] = useState<Record<string, string | null>>({});

  const conversations = useQuery({
    queryKey: ["conversations", filter],
    queryFn: () =>
      api<Paginated<ConversationDto>>(
        `/conversations?state=${filter.state}${filter.mine ? `&assigneeId=${agent!.id}` : ""}&sort=lastMessage`,
      ),
    refetchInterval: 30_000,
  });

  // ---- realtime wiring ----
  useEffect(() => {
    let mounted = true;
    startConnection().then((conn) => {
      if (!mounted) return;
      conn.on("messageCreated", (message: MessageDto) => {
        queryClient.invalidateQueries({ queryKey: ["conversations"] });
        queryClient.setQueryData<MessageDto[]>(["messages", message.conversationId], (old) =>
          old && !old.some((m) => m.id === message.id) ? [...old, message] : old,
        );
        setTyping((t) => ({ ...t, [message.conversationId]: null }));
      });
      conn.on("conversationUpdated", () => queryClient.invalidateQueries({ queryKey: ["conversations"] }));
      conn.on("slaBreached", () => queryClient.invalidateQueries({ queryKey: ["conversations"] }));
      conn.on("messagesRead", ({ conversationId }: { conversationId: string }) =>
        queryClient.invalidateQueries({ queryKey: ["messages", conversationId] }),
      );
      conn.on("presenceChanged", () => queryClient.invalidateQueries({ queryKey: ["agents"] }));
      conn.on("typing", (p: { conversationId: string; source: string; name: string; isTyping: boolean }) => {
        if (p.source === "visitor") {
          setTyping((t) => ({ ...t, [p.conversationId]: p.isTyping ? p.name : null }));
        }
      });
      conn.on("viewerJoined", (v: { conversationId: string; agentUserId: string; agentName: string }) =>
        setViewers((prev) => ({
          ...prev,
          [v.conversationId]: [
            ...(prev[v.conversationId] ?? []).filter((x) => x.agentUserId !== v.agentUserId),
            { agentUserId: v.agentUserId, agentName: v.agentName },
          ],
        })),
      );
      conn.on("viewerLeft", (v: { conversationId: string; agentUserId: string }) =>
        setViewers((prev) => ({
          ...prev,
          [v.conversationId]: (prev[v.conversationId] ?? []).filter((x) => x.agentUserId !== v.agentUserId),
        })),
      );
      conn.onreconnected(() => {
        // DB is source-of-truth: refetch everything we may have missed while offline.
        queryClient.invalidateQueries();
      });
    });
    return () => {
      mounted = false;
    };
  }, [queryClient]);

  const selected = conversations.data?.items.find((c) => c.id === selectedId) ?? null;

  return (
    <div className="inbox">
      <aside className="pane list-pane">
        <div className="filters">
          {(["Open", "Snoozed", "Closed"] as ConversationState[]).map((s) => (
            <button
              key={s}
              className={filter.state === s ? "chip active" : "chip"}
              onClick={() => setFilter((f) => ({ ...f, state: s }))}
            >
              {s}
            </button>
          ))}
          <button
            className={filter.mine ? "chip active" : "chip"}
            onClick={() => setFilter((f) => ({ ...f, mine: !f.mine }))}
          >
            Mine
          </button>
        </div>
        {conversations.isLoading && <div className="page-loading">Loading…</div>}
        {conversations.isError && <div className="error page-loading">Failed to load inbox.</div>}
        {conversations.data?.items.length === 0 && <div className="page-loading muted">No conversations.</div>}
        <ul className="conversation-list">
          {conversations.data?.items.map((c) => (
            <ConversationRow key={c.id} c={c} active={c.id === selectedId} onClick={() => setSelectedId(c.id)} />
          ))}
        </ul>
      </aside>

      {selected ? (
        <ConversationView
          key={selected.id}
          conversation={selected}
          viewers={(viewers[selected.id] ?? []).filter((v) => v.agentUserId !== agent!.id)}
          visitorTyping={typing[selected.id] ?? null}
        />
      ) : (
        <main className="pane center-pane empty">Select a conversation</main>
      )}

      {selected && <VisitorPanel conversation={selected} />}
    </div>
  );
}

function ConversationRow({ c, active, onClick }: { c: ConversationDto; active: boolean; onClick: () => void }) {
  return (
    <li className={active ? "conv-row active" : "conv-row"} onClick={onClick}>
      <div className="conv-top">
        <strong>{c.visitor.displayName}</strong>
        {c.activeSlaTimer && <SlaCountdown breachAtUtc={c.activeSlaTimer.breachAtUtc} />}
        {c.hasBreachedSla && <span className="badge breach">SLA breached</span>}
      </div>
      <div className="conv-preview muted">{c.lastMessagePreview ?? c.subject ?? "(no messages)"}</div>
      <div className="conv-meta muted">
        {c.teamName ?? "unrouted"} · {c.assigneeName ?? "unassigned"}
      </div>
    </li>
  );
}

/** Live countdown to SLA breach. */
function SlaCountdown({ breachAtUtc }: { breachAtUtc: string }) {
  const [, force] = useState(0);
  useEffect(() => {
    const t = setInterval(() => force((n) => n + 1), 1000);
    return () => clearInterval(t);
  }, []);
  const remaining = new Date(breachAtUtc).getTime() - Date.now();
  const overdue = remaining < 0;
  const abs = Math.abs(remaining);
  const minutes = Math.floor(abs / 60000);
  const seconds = Math.floor((abs % 60000) / 1000);
  return (
    <span className={overdue ? "badge breach" : minutes < 5 ? "badge warn" : "badge"}>
      {overdue ? "-" : ""}
      {minutes}:{String(seconds).padStart(2, "0")}
    </span>
  );
}

function ConversationView({
  conversation,
  viewers,
  visitorTyping,
}: {
  conversation: ConversationDto;
  viewers: { agentUserId: string; agentName: string }[];
  visitorTyping: string | null;
}) {
  const queryClient = useQueryClient();
  const [body, setBody] = useState("");
  const [isNote, setIsNote] = useState(false);
  const [cannedOpen, setCannedOpen] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  const messages = useQuery({
    queryKey: ["messages", conversation.id],
    queryFn: () => api<MessageDto[]>(`/conversations/${conversation.id}/messages`),
  });

  const canned = useQuery({
    queryKey: ["canned-replies"],
    queryFn: () => api<CannedReplyDto[]>("/canned-replies"),
  });

  // join/leave the conversation group (collision detection + typing)
  useEffect(() => {
    let conn: Awaited<ReturnType<typeof startConnection>> | null = null;
    startConnection().then(async (c) => {
      conn = c;
      await c.invoke("JoinConversation", conversation.id).catch(() => {});
    });
    post(`/conversations/${conversation.id}/read`, {}).catch(() => {});
    return () => {
      conn?.invoke("LeaveConversation", conversation.id).catch(() => {});
    };
  }, [conversation.id]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages.data?.length]);

  const send = useMutation({
    mutationFn: () =>
      post<MessageDto>(`/conversations/${conversation.id}/messages`, { body, isInternalNote: isNote }),
    onSuccess: (message) => {
      setBody("");
      queryClient.setQueryData<MessageDto[]>(["messages", conversation.id], (old) =>
        old && !old.some((m) => m.id === message.id) ? [...old, message] : old,
      );
    },
  });

  const changeState = useMutation({
    mutationFn: (state: { state: ConversationState; snoozeUntilUtc?: string }) =>
      put(`/conversations/${conversation.id}/state`, state),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["conversations"] }),
  });

  // typing indicator (debounced stop)
  const typingTimeout = useRef<number | undefined>(undefined);
  function onTyping() {
    startConnection().then((c) => c.invoke("Typing", conversation.id, true).catch(() => {}));
    window.clearTimeout(typingTimeout.current);
    typingTimeout.current = window.setTimeout(
      () => startConnection().then((c) => c.invoke("Typing", conversation.id, false).catch(() => {})),
      2000,
    );
  }

  // "/shortcut" canned reply insertion
  const slashMatch = useMemo(() => /^\/([a-zA-Z0-9_-]*)$/.exec(body), [body]);
  const suggestions = useMemo(
    () =>
      slashMatch && canned.data
        ? canned.data.filter((r) => r.shortcut.toLowerCase().startsWith(slashMatch[1].toLowerCase()))
        : [],
    [slashMatch, canned.data],
  );

  async function insertCanned(reply: CannedReplyDto) {
    const rendered = await api<{ body: string }>(
      `/canned-replies/${reply.id}/render?conversationId=${conversation.id}`,
    );
    setBody(rendered.body);
    setCannedOpen(false);
  }

  return (
    <main className="pane center-pane">
      <header className="conv-header">
        <div>
          <strong>{conversation.visitor.displayName}</strong>
          <span className="muted"> · {conversation.teamName ?? "unrouted"}</span>
          {conversation.activeSlaTimer && <SlaCountdown breachAtUtc={conversation.activeSlaTimer.breachAtUtc} />}
        </div>
        {viewers.length > 0 && (
          <div className="collision">⚠ {viewers.map((v) => v.agentName).join(", ")} is already viewing this</div>
        )}
        <div className="actions">
          {conversation.state !== "Closed" && (
            <>
              <button
                onClick={() =>
                  changeState.mutate({
                    state: "Snoozed",
                    snoozeUntilUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
                  })
                }
              >
                Snooze 1h
              </button>
              <button onClick={() => changeState.mutate({ state: "Closed" })}>Close</button>
            </>
          )}
          {conversation.state !== "Open" && (
            <button onClick={() => changeState.mutate({ state: "Open" })}>Reopen</button>
          )}
        </div>
      </header>

      <div className="messages">
        {messages.isLoading && <div className="page-loading">Loading messages…</div>}
        {messages.isError && <div className="error page-loading">Failed to load messages.</div>}
        {messages.data?.map((m) => (
          <div
            key={m.id}
            className={`message ${m.senderType === "Agent" ? "from-agent" : "from-visitor"} ${
              m.isInternalNote ? "note" : ""
            }`}
          >
            <div className="message-meta muted">
              {m.senderName} · {new Date(m.createdAtUtc).toLocaleTimeString()}
              {m.isInternalNote && " · internal note"}
              {m.senderType === "Agent" && !m.isInternalNote && m.readByVisitorAtUtc && " · ✓ read"}
            </div>
            <div className="message-body">{m.body}</div>
          </div>
        ))}
        {visitorTyping && <div className="typing muted">{visitorTyping} is typing…</div>}
        <div ref={bottomRef} />
      </div>

      <footer className="composer">
        {(cannedOpen || suggestions.length > 0) && (
          <div className="canned-popup card">
            {(suggestions.length > 0 ? suggestions : canned.data ?? []).map((r) => (
              <button key={r.id} className="canned-item" onClick={() => insertCanned(r)}>
                <code>/{r.shortcut}</code> {r.title}
              </button>
            ))}
          </div>
        )}
        <div className="composer-row">
          <label className="row note-toggle">
            <input type="checkbox" checked={isNote} onChange={(e) => setIsNote(e.target.checked)} /> Note
          </label>
          <textarea
            className={isNote ? "note-bg" : ""}
            value={body}
            placeholder={isNote ? "Internal note (visitor never sees this)…" : "Reply… (type / for canned replies)"}
            onChange={(e) => {
              setBody(e.target.value);
              onTyping();
            }}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey && body.trim()) {
                e.preventDefault();
                send.mutate();
              }
            }}
            rows={3}
          />
          <button onClick={() => setCannedOpen((o) => !o)} title="Canned replies">
            ⚡
          </button>
          <button disabled={!body.trim() || send.isPending} onClick={() => send.mutate()}>
            Send
          </button>
        </div>
        {send.isError && <div className="error">{(send.error as Error).message}</div>}
      </footer>
    </main>
  );
}

function VisitorPanel({ conversation }: { conversation: ConversationDto }) {
  const queryClient = useQueryClient();
  const teams = useQuery({ queryKey: ["teams"], queryFn: () => api<TeamDto[]>("/teams") });
  const agents = useQuery({ queryKey: ["agents"], queryFn: () => api<AgentDto[]>("/agents") });

  const assign = useMutation({
    mutationFn: (req: { agentUserId: string | null; teamId: string | null }) =>
      put(`/conversations/${conversation.id}/assignee`, req),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["conversations"] }),
  });

  const attributes = useMemo(() => {
    try {
      return conversation.visitor.attributesJson ? JSON.parse(conversation.visitor.attributesJson) : null;
    } catch {
      return null;
    }
  }, [conversation.visitor.attributesJson]);

  return (
    <aside className="pane profile-pane">
      <h3>{conversation.visitor.displayName}</h3>
      <dl>
        <dt>Email</dt>
        <dd>{conversation.visitor.email ?? "—"}</dd>
        <dt>Last seen</dt>
        <dd>{new Date(conversation.visitor.lastSeenAtUtc).toLocaleString()}</dd>
        <dt>Source page</dt>
        <dd className="wrap">{conversation.sourceUrl ?? "—"}</dd>
        <dt>State</dt>
        <dd>{conversation.state}</dd>
      </dl>

      <h4>Assignment</h4>
      <label>
        Team
        <select
          value={conversation.teamId ?? ""}
          onChange={(e) =>
            assign.mutate({ agentUserId: conversation.assigneeId, teamId: e.target.value || null })
          }
        >
          <option value="">—</option>
          {teams.data?.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
      </label>
      <label>
        Agent
        <select
          value={conversation.assigneeId ?? ""}
          onChange={(e) =>
            assign.mutate({ agentUserId: e.target.value || null, teamId: conversation.teamId })
          }
        >
          <option value="">unassigned</option>
          {agents.data?.map((a) => (
            <option key={a.id} value={a.id}>
              {a.displayName} ({a.presence})
            </option>
          ))}
        </select>
      </label>

      {attributes && (
        <>
          <h4>Attributes</h4>
          <pre className="card attrs">{JSON.stringify(attributes, null, 2)}</pre>
        </>
      )}
    </aside>
  );
}
