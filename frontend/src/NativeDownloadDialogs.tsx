import { useEffect, useState } from "react";
import { CalendarClock, Download, Link2, ShieldCheck, X } from "lucide-react";
import { sendDirectDownload, sendSchedule, type DownloadRow, type ScheduleRecurrence } from "./nativeBridge";

/** The existing .NET download engine owns all files and queues.
 * React collects the user's intent; the native host validates and persists it. */
export function AddDownloadDialog({ onClose, onFallback, onError, queueDefault }: {
  onClose: () => void; onFallback: () => void; onError: (message: string) => void; queueDefault: boolean
}) {
  const [address, setAddress] = useState("");
  const [queued, setQueued] = useState(queueDefault);
  useEffect(() => {
    const close = (event: KeyboardEvent) => { if (event.key === "Escape") onClose(); };
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [onClose]);
  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const input = address.trim();
    if (!/^https?:\/\//i.test(input) || input.length > 3500) {
      onError("Enter a direct HTTP or HTTPS download link (up to 3,500 characters).");
      return;
    }
    if (!sendDirectDownload(input, queued)) {
      onError("RageDesk's native download service is unavailable.");
      return;
    }
    onClose();
  };
  return <div className="native-modal-backdrop" onMouseDown={event=>{if(event.target===event.currentTarget)onClose();}}>
    <form className="native-modal" onSubmit={submit} role="dialog" aria-modal="true" aria-labelledby="new-download-title">
      <header><div className="native-modal-icon"><Download size={24}/></div>
        <div><h2 id="new-download-title">New Download</h2><p>Direct downloads use the existing RageDesk engine.</p></div>
        <button type="button" className="native-modal-close" onClick={onClose} aria-label="Close"><X size={20}/></button></header>
      <label className="native-modal-label" htmlFor="direct-file-url"><Link2 size={16}/> Direct HTTP(S) file link</label>
      <input autoFocus required id="direct-file-url" type="url" maxLength={3500} placeholder="https://example.com/archive.zip"
        value={address} onChange={event=>setAddress(event.target.value)} autoComplete="off" spellCheck={false}/>
      <p className="native-modal-hint">RageDesk chooses a safe filename in your saved download folder. For custom names, checksums, batches or media, use Advanced Options.</p>
      <label className="native-queue-choice"><input type="checkbox" checked={queued} onChange={e=>setQueued(e.target.checked)}/> Add to queue instead of starting now</label>
      <div className="native-modal-privacy"><ShieldCheck size={15}/> Link stays local. Files are never sent to React cloud services.</div>
      <footer><button type="button" className="native-secondary" onClick={onFallback}>Advanced Options</button>
        <button type="button" className="native-secondary" onClick={onClose}>Cancel</button>
        <button className="native-primary" type="submit">{queued ? "Add to queue" : "Start download"}</button>
      </footer>
    </form>
  </div>;
}

export function ScheduleDownloadDialog({ item, onClose, onError }: {
  item: DownloadRow; onClose: () => void; onError: (message: string) => void
}) {
  const [localTime, setLocalTime] = useState("");
  const [recurrence, setRecurrence] = useState<ScheduleRecurrence>("Once");
  useEffect(() => {
    const close = (event: KeyboardEvent) => {if(event.key === "Escape")onClose();};
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [onClose]);
  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(localTime) ||
      !sendSchedule(item.id, localTime, recurrence)) {
      onError("Choose a future local date and time and verify RageDesk is connected.");
      return;
    }
    onClose();
  };
  return <div className="native-modal-backdrop" onMouseDown={event=>{if(event.target===event.currentTarget)onClose();}}>
    <form className="native-modal" onSubmit={submit} role="dialog" aria-modal="true" aria-labelledby="schedule-download-title">
      <header><div className="native-modal-icon"><CalendarClock size={24}/></div>
        <div><h2 id="schedule-download-title">Schedule Download</h2><p title={item.name}>{item.name}</p></div>
        <button className="native-modal-close" type="button" onClick={onClose} aria-label="Close"><X size={20}/></button></header>
      <label className="native-modal-label" htmlFor="schedule-date">Local date & time</label>
      <input autoFocus id="schedule-date" type="datetime-local" required value={localTime} onChange={event=>setLocalTime(event.target.value)}/>
      <label className="native-modal-label" htmlFor="schedule-recurrence">Recurrence</label>
      <select id="schedule-recurrence" value={recurrence} onChange={e=>setRecurrence(e.target.value as ScheduleRecurrence)}>
        <option value="Once">Once</option><option value="Daily">Daily</option>
        <option value="Weekdays">Weekdays</option><option value="Weekly">Weekly</option>
      </select>
      <p className="native-modal-hint">RageDesk must be running to start the queued file. Ambiguous daylight-saving times are rejected rather than changed silently.</p>
      <footer><button type="button" className="native-secondary" onClick={onClose}>Cancel</button>
        {item.isScheduled&&<button type="button" className="native-secondary" onClick={()=>{
          if(sendSchedule(item.id,"","Once",true))onClose();
          else onError("Native scheduler unavailable.");
        }}>Remove schedule</button>}
        <button type="submit" className="native-primary">Save schedule</button>
      </footer>
    </form>
  </div>;
}
