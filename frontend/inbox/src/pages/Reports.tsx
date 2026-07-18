import { useQuery } from "@tanstack/react-query";
import { api } from "../api";

interface Overview {
  newConversations: number;
  closedConversations: number;
  openConversations: number;
  medianFirstResponseMinutes: number | null;
  medianResolutionMinutes: number | null;
  agentStats: {
    agentUserId: string;
    agentName: string;
    conversationsHandled: number;
    messagesSent: number;
    medianFirstResponseMinutes: number | null;
  }[];
}

interface SlaRow {
  teamId: string | null;
  teamName: string;
  kind: string;
  met: number;
  breached: number;
  metPercent: number;
}

const fmt = (v: number | null) => (v == null ? "—" : `${v.toFixed(1)} min`);

export default function ReportsPage() {
  const overview = useQuery({
    queryKey: ["report-overview"],
    queryFn: () => api<Overview>("/reports/overview"),
  });
  const sla = useQuery({ queryKey: ["report-sla"], queryFn: () => api<SlaRow[]>("/reports/sla") });

  if (overview.isLoading || sla.isLoading) return <div className="page-loading">Loading reports…</div>;
  if (overview.isError || sla.isError) return <div className="error page-loading">Failed to load reports.</div>;

  const o = overview.data!;
  return (
    <main className="reports">
      <h2>Last 30 days</h2>
      <div className="stat-row">
        <div className="card stat">
          <strong>{o.newConversations}</strong>
          <span>New conversations</span>
        </div>
        <div className="card stat">
          <strong>{o.openConversations}</strong>
          <span>Currently open</span>
        </div>
        <div className="card stat">
          <strong>{fmt(o.medianFirstResponseMinutes)}</strong>
          <span>Median first response</span>
        </div>
        <div className="card stat">
          <strong>{fmt(o.medianResolutionMinutes)}</strong>
          <span>Median resolution</span>
        </div>
      </div>

      <h2>SLA by team</h2>
      {sla.data!.length === 0 ? (
        <p className="muted">No completed SLA timers in this period yet.</p>
      ) : (
        <table className="card table">
          <thead>
            <tr>
              <th>Team</th>
              <th>Timer</th>
              <th>Met</th>
              <th>Breached</th>
              <th>Met %</th>
            </tr>
          </thead>
          <tbody>
            {sla.data!.map((r, i) => (
              <tr key={i}>
                <td>{r.teamName}</td>
                <td>{r.kind}</td>
                <td>{r.met}</td>
                <td>{r.breached}</td>
                <td>{r.metPercent}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2>Per agent</h2>
      {o.agentStats.length === 0 ? (
        <p className="muted">No agent activity in this period yet.</p>
      ) : (
        <table className="card table">
          <thead>
            <tr>
              <th>Agent</th>
              <th>Conversations</th>
              <th>Messages</th>
              <th>Median first response</th>
            </tr>
          </thead>
          <tbody>
            {o.agentStats.map((a) => (
              <tr key={a.agentUserId}>
                <td>{a.agentName}</td>
                <td>{a.conversationsHandled}</td>
                <td>{a.messagesSent}</td>
                <td>{fmt(a.medianFirstResponseMinutes)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  );
}
