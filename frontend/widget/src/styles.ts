/** Injected at runtime so the host page needs no extra CSS file. */
export const css = `
#netintercom-widget-root { all: initial; }
.ni-bubble-btn { position: fixed; bottom: 20px; right: 20px; width: 56px; height: 56px; border-radius: 50%;
  border: none; background: #2563eb; color: #fff; font-size: 24px; cursor: pointer; z-index: 999999;
  box-shadow: 0 4px 14px rgba(0,0,0,.25); }
.ni-panel { position: fixed; bottom: 90px; right: 20px; width: 340px; height: 480px; background: #fff;
  border-radius: 12px; box-shadow: 0 8px 30px rgba(0,0,0,.2); display: flex; flex-direction: column;
  overflow: hidden; z-index: 999999; font-family: system-ui, sans-serif; font-size: 14px; color: #1f2933; }
.ni-header { display: flex; align-items: center; gap: 10px; padding: 12px; background: #2563eb; color: #fff; }
.ni-sub { font-size: 12px; opacity: .85; }
.ni-dot { width: 10px; height: 10px; border-radius: 50%; background: #9ca3af; }
.ni-dot.on { background: #22c55e; }
.ni-x { margin-left: auto; background: none; border: none; color: #fff; font-size: 20px; cursor: pointer; }
.ni-messages { flex: 1; overflow-y: auto; padding: 12px; display: flex; flex-direction: column; gap: 8px; background: #f9fafb; }
.ni-info { text-align: center; color: #6b7280; margin-top: 12px; }
.ni-msg { max-width: 80%; display: flex; flex-direction: column; }
.ni-mine { align-self: flex-end; align-items: flex-end; }
.ni-theirs { align-self: flex-start; }
.ni-sender { font-size: 11px; color: #6b7280; margin-bottom: 2px; }
.ni-bubble { padding: 8px 12px; border-radius: 12px; background: #fff; border: 1px solid #e5e7eb; white-space: pre-wrap; }
.ni-mine .ni-bubble { background: #2563eb; color: #fff; border-color: #2563eb; }
.ni-typing { font-size: 12px; color: #6b7280; font-style: italic; }
.ni-composer { display: flex; gap: 8px; padding: 10px; border-top: 1px solid #e5e7eb; background: #fff; }
.ni-composer textarea { flex: 1; resize: none; border: 1px solid #d1d5db; border-radius: 8px; padding: 8px;
  font: inherit; }
.ni-composer button { border: none; background: #2563eb; color: #fff; border-radius: 8px; width: 40px; cursor: pointer; }
.ni-composer button:disabled { opacity: .5; }
`;
