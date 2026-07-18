import { useCallback, useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { WidgetApi, type MessageDto, type WidgetConversation } from "./api";
import type { WidgetConfig } from "./main";
import { css } from "./styles";

export function Widget({ config }: { config: WidgetConfig }) {
  const [open, setOpen] = useState(false);
  const [api] = useState(() => new WidgetApi(config.apiBase));
  const [conversation, setConversation] = useState<WidgetConversation | null>(null);
  const [messages, setMessages] = useState<MessageDto[]>([]);
  const [body, setBody] = useState("");
  const [agentTyping, setAgentTyping] = useState<string | null>(null);
  const [teamOnline, setTeamOnline] = useState(false);
  const [status, setStatus] = useState<"loading" | "ready" | "error">("loading");
  const connRef = useRef<signalR.HubConnection | null>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  const addMessage = useCallback((m: MessageDto) => {
    setMessages((old) => (old.some((x) => x.id === m.id) ? old : [...old, m]));
  }, []);

  // session bootstrap: resume identity + history, then connect the hub
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const session = await api.startSession(config.user, config.attributes);
        if (cancelled) return;
        setConversation(session.conversation);
        setMessages(session.messages);
        setStatus("ready");

        const conn = new signalR.HubConnectionBuilder()
          .withUrl(`${config.apiBase}/hubs/visitor`, { accessTokenFactory: () => api.token_ ?? "" })
          .withAutomaticReconnect()
          .build();
        conn.on("messageCreated", (m: MessageDto) => {
          addMessage(m);
          setAgentTyping(null);
        });
        conn.on("typing", (p: { name: string; isTyping: boolean; source: string }) => {
          if (p.source === "agent") setAgentTyping(p.isTyping ? p.name : null);
        });
        conn.on("teamPresence", (p: { anyAgentOnline: boolean }) => setTeamOnline(p.anyAgentOnline));
        conn.on("conversationUpdated", (c: WidgetConversation) =>
          setConversation((old) => (old && old.id === c.id ? { ...old, ...c } : old)),
        );
        conn.on("messagesRead", () => {
          /* could flip read ticks; transcript refetch covers it */
        });
        conn.onreconnected(async () => {
          // missed-message catch-up from the DB
          if (session.conversation) {
            const latest = await api.getMessages(session.conversation.id);
            setMessages(latest);
          }
        });
        await conn.start();
        connRef.current = conn;
        if (session.conversation) {
          await conn.invoke("JoinConversation", session.conversation.id);
        }
      } catch {
        if (!cancelled) setStatus("error");
      }
    })();
    return () => {
      cancelled = true;
      connRef.current?.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
    if (open && conversation) api.markRead(conversation.id).catch(() => {});
  }, [messages.length, open, conversation, api]);

  async function send() {
    const text = body.trim();
    if (!text) return;
    setBody("");
    const result = await api.sendMessage(text, conversation?.id ?? null);
    addMessage(result.message);
    if (!conversation || conversation.id !== result.conversation.id) {
      setConversation(result.conversation);
      await connRef.current?.invoke("JoinConversation", result.conversation.id).catch(() => {});
    }
  }

  const typingTimeout = useRef<number | undefined>(undefined);
  function onTyping() {
    if (!conversation) return;
    connRef.current?.invoke("Typing", conversation.id, true).catch(() => {});
    window.clearTimeout(typingTimeout.current);
    typingTimeout.current = window.setTimeout(
      () => connRef.current?.invoke("Typing", conversation!.id, false).catch(() => {}),
      2000,
    );
  }

  return (
    <>
      <style>{css}</style>
      {open && (
        <div className="ni-panel">
          <div className="ni-header">
            <span className={`ni-dot ${teamOnline ? "on" : ""}`} />
            <div>
              <strong>{conversation?.assigneeName ?? "Support"}</strong>
              <div className="ni-sub">{teamOnline ? "We're online" : "We'll reply by email/asap"}</div>
            </div>
            <button className="ni-x" onClick={() => setOpen(false)}>
              ×
            </button>
          </div>
          <div className="ni-messages">
            {status === "loading" && <div className="ni-info">Connecting…</div>}
            {status === "error" && <div className="ni-info">Chat is unavailable right now.</div>}
            {status === "ready" && messages.length === 0 && (
              <div className="ni-info">Hi 👋 — ask us anything!</div>
            )}
            {messages.map((m) => (
              <div key={m.id} className={`ni-msg ${m.senderType === "Visitor" ? "ni-mine" : "ni-theirs"}`}>
                {m.senderType !== "Visitor" && <div className="ni-sender">{m.senderName}</div>}
                <div className="ni-bubble">{m.body}</div>
                {m.senderType === "Visitor" && m.readByVisitorAtUtc === null && <span />}
              </div>
            ))}
            {agentTyping && <div className="ni-typing">{agentTyping} is typing…</div>}
            <div ref={bottomRef} />
          </div>
          <div className="ni-composer">
            <textarea
              rows={1}
              value={body}
              placeholder="Write a message…"
              onChange={(e) => {
                setBody(e.target.value);
                onTyping();
              }}
              onKeyDown={(e) => {
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  send();
                }
              }}
            />
            <button onClick={send} disabled={!body.trim() || status !== "ready"}>
              ➤
            </button>
          </div>
        </div>
      )}
      <button className="ni-bubble-btn" onClick={() => setOpen((o) => !o)} aria-label="Open chat">
        {open ? "×" : "💬"}
      </button>
    </>
  );
}
