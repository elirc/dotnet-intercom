import { createRoot } from "react-dom/client";
import { Widget } from "./Widget";

export interface WidgetConfig {
  apiBase: string;
  user?: { externalId: string; name?: string; email?: string };
  attributes?: Record<string, unknown>;
}

/** Entry point exposed as window.NetIntercomWidget.init(...) by the IIFE build. */
export function init(config: WidgetConfig) {
  const host = document.createElement("div");
  host.id = "netintercom-widget-root";
  document.body.appendChild(host);
  createRoot(host).render(<Widget config={config} />);
}
