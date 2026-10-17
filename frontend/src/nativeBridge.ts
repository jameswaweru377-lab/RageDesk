export type DownloadRow = {
  id: string; name: string; host: string; category: string; status: string;
  receivedBytes: number; totalBytes: number | null; percent: number | null;
  bytesPerSecond: number; eta: string; connections: number; requestedConnections: number;
  added: string; addedAt: string;
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
type NativeEnvelope = { version: number; type: "dashboard.snapshot"; payload: DashboardSnapshot };

interface NativeWebView {
  postMessage: (message: unknown) => void;
  addEventListener: (type: "message", cb: (e: MessageEvent<NativeEnvelope>) => void) => void;
  removeEventListener: (type: "message", cb: (e: MessageEvent<NativeEnvelope>) => void) => void;
}
declare global { interface Window { chrome?: { webview?: NativeWebView } } }

/** Only the trusted local C# host can publish snapshots. This slice deliberately has no command bridge. */
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
