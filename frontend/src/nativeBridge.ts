export type DownloadRow = {
  id: string; name: string; host: string; category: string; status: string;
  receivedBytes: number; totalBytes: number | null; percent: number | null;
  bytesPerSecond: number; eta: string; connections: number; requestedConnections: number;
  added: string; addedAt: string; isScheduled: boolean;
};
export type DashboardSnapshot = {
  theme: "Cobalt" | "Arctic";
  summary: { currentSpeed: string; downloadedToday: string; timeRemaining: string };
  counts: { all: number; downloading: number; completed: number; paused: number; queued: number; failed: number };
  categoryCounts: Record<string, number>;
  siteCounts: Record<string, number>;
  scheduledCount: number;
  rows: DownloadRow[];
  sources: { host: string; bytes: number }[];
  recent: { id: string; added: string; description: string }[];
  speedSamples: number[];
  storage: { freeBytes: number; totalBytes: number } | null;
  settings: { startImmediately: boolean; defaultConnections: number; maxConcurrentDownloads: number; defaultBandwidthKiBps: number; proxyEnabled: boolean };
};
export type DownloadCommand = "pause" | "resume" | "stop" | "startQueue" | "openFolder" | "openFile" | "details" | "schedule" | "earlier" | "later" | "removeHistory";
export type BrowserCommand = "connect" | "check" | "extensionFolder" | "extensionsPage";
export type PreferenceKey = "startImmediately" | "theme" | "defaultConnections" | "maxConcurrentDownloads" | "bandwidthLimit";
export type NativeActionResult = { requestId: string; message: string };

type NativeEnvelope =
  | { version: number; type: "dashboard.snapshot"; payload: DashboardSnapshot }
  | { version: number; type: "ui.actionResult"; payload: NativeActionResult }
  | { version: number; type: "ui.browserCaptured"; payload: { address: string } };

interface NativeWebView {
  postMessage: (message: unknown) => void;
  addEventListener: (type: "message", cb: (e: MessageEvent<NativeEnvelope>) => void) => void;
  removeEventListener: (type: "message", cb: (e: MessageEvent<NativeEnvelope>) => void) => void;
}
declare global { interface Window { chrome?: { webview?: NativeWebView } } }

/** Only two explicitly allowlisted presentation commands can leave the React preview.
 * Both open the existing native WPF dialogs and cannot commit any changes directly. */
export function openNativeDialog(type: "ui.openSettings" | "ui.openAddDownload"): void {
  window.chrome?.webview?.postMessage({ version: 1, type });
}

/** Send only catalog IDs, never file paths or signed URLs. Native C# validates all IDs
 * and handles state transitions. The browser extension stays a separate opt-in integration. */
export function sendDownloadAction(action: DownloadCommand, ids: string[]): boolean {
  if (!window.chrome?.webview || ids.length > 64) return false;
  window.chrome.webview.postMessage({
    version: 1, type: "ui.download", requestId: crypto.randomUUID(), action, ids
  });
  return true;
}
export function sendBrowserAction(action: BrowserCommand): boolean {
  if (!window.chrome?.webview) return false;
  window.chrome.webview.postMessage({
    version: 1, type: "ui.browser", requestId: crypto.randomUUID(), action
  });
  return true;
}
export function sendPreference(key: PreferenceKey, value: boolean | string | number): boolean {
  if (!window.chrome?.webview) return false;
  window.chrome.webview.postMessage({
    version: 1, type: "ui.setting", requestId: crypto.randomUUID(), action: key, value
  });
  return true;
}
/** A direct URL is sent only to the bundled local C# host after explicit submission.
 * It is never included in the periodic dashboard snapshots or remote assets. */
export function sendDirectDownload(address: string, queued: boolean): boolean {
  if (!window.chrome?.webview || !/^https?:\/\//i.test(address) || address.length > 3500)
    return false;
  window.chrome.webview.postMessage({
    version: 1, type: "ui.submit", requestId: crypto.randomUUID(),
    action: "addDownload", address, queued
  });
  return true;
}
export type ScheduleRecurrence = "Once" | "Daily" | "Weekdays" | "Weekly";
export function sendSchedule(id: string, localTime: string, recurrence: ScheduleRecurrence, remove=false): boolean {
  if (!window.chrome?.webview || !/^[0-9a-f]{32}$/i.test(id)) return false;
  window.chrome.webview.postMessage({
    version: 1, type: "ui.schedule", requestId: crypto.randomUUID(),
    action: "schedule", id, localTime, recurrence, remove
  });
  return true;
}
export function chooseDownloadFolder(): boolean {
  if (!window.chrome?.webview) return false;
  window.chrome.webview.postMessage({
    version: 1, type: "ui.folder", requestId: crypto.randomUUID(), action: "browse"
  });
  return true;
}

export function subscribeToActionResults(onResult: (message: string) => void): () => void {
  const bridge = window.chrome?.webview;
  if (!bridge) return () => undefined;
  const listener = (event: MessageEvent<NativeEnvelope>) => {
    const response = event.data;
    if (response?.version === 1 && response.type === "ui.actionResult" &&
      typeof response.payload?.message === "string") onResult(response.payload.message);
  };
  bridge.addEventListener("message", listener);
  return () => bridge.removeEventListener("message", listener);
}

/** Manual browser handoff is a user-selected link from the local C# inbox,
 * not a web-page event or remote fetch. It is not persisted by React. */
export function subscribeToCapturedLinks(onCaptured: (address: string) => void): () => void {
  const bridge = window.chrome?.webview;
  if (!bridge) return () => undefined;
  const listener = (event: MessageEvent<NativeEnvelope>) => {
    const msg = event.data;
    if (msg?.version === 1 && msg.type === "ui.browserCaptured" &&
        typeof msg.payload?.address === "string" &&
        msg.payload.address.length <= 3500 && /^https?:\/\//i.test(msg.payload.address))
      onCaptured(msg.payload.address);
  };
  bridge.addEventListener("message", listener);
  return () => bridge.removeEventListener("message", listener);
}

/** The trusted local C# host publishes bounded and versioned snapshots. */
export function subscribeToNativeDashboard(
  onData: (data: DashboardSnapshot) => void,
  onConnection: (connection: "connecting" | "connected" | "unavailable") => void
): () => void {
  const bridge = window.chrome?.webview;
  if (!bridge) { onConnection("unavailable"); return () => undefined; }
  let lastVersion = -1;
  const onMessage = (event: MessageEvent<NativeEnvelope>) => {
    const envelope = event.data;
    if (envelope?.version !== 1 || envelope.type !== "dashboard.snapshot" ||
        !Array.isArray(envelope.payload?.rows) || !envelope.payload?.counts) return;
    // Do not allow an old or malformed packet to replace a valid snapshot.
    // The receiver is in a fixed local virtual origin; outgoing C# packets are versioned.
    lastVersion = envelope.version;
    onData(envelope.payload);
    onConnection("connected");
  };
  bridge.addEventListener("message", onMessage);
  bridge.postMessage({ version: 1, type: "dashboard.ready" });
  void lastVersion; // Explicitly reserved for future sequence-based stream updates.
  return () => bridge.removeEventListener("message", onMessage);
}
