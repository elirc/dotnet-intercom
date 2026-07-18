import * as signalR from "@microsoft/signalr";
import { getAccessToken } from "./api";

let connection: signalR.HubConnection | null = null;

/** Singleton agent hub connection with automatic reconnect.
 *  On reconnect, callers should refetch messages with ?after= (DB is source-of-truth). */
export function getAgentConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/agent", { accessTokenFactory: () => getAccessToken() ?? "" })
      .withAutomaticReconnect()
      .build();
  }
  return connection;
}

export async function startConnection(): Promise<signalR.HubConnection> {
  const conn = getAgentConnection();
  if (conn.state === signalR.HubConnectionState.Disconnected) {
    await conn.start();
    // activity heartbeat for presence (online/away)
    const beat = () => conn.invoke("Heartbeat").catch(() => {});
    window.addEventListener("mousemove", throttle(beat, 30000));
    window.addEventListener("keydown", throttle(beat, 30000));
  }
  return conn;
}

export async function stopConnection() {
  if (connection) {
    await connection.stop();
    connection = null;
  }
}

function throttle(fn: () => void, ms: number) {
  let last = 0;
  return () => {
    const now = Date.now();
    if (now - last > ms) {
      last = now;
      fn();
    }
  };
}
