import { useEffect, useMemo, useState } from "react";
import {
  Activity, Archive, BarChart3, CalendarClock, Check, CheckCircle2,
  ChevronDown, Clock3, CloudDownload, Code2, Download, ExternalLink,
  FileArchive, FileImage, FileText, Filter, Folder, FolderOpen,
  Globe2, HardDrive, Image as ImageIcon, LayoutGrid, List, Menu,
  MoreHorizontal, Music2, Pause, Play, Search, Settings2,
  ShieldCheck, Video, XCircle, Home, Trash2, Package, MonitorDown,
  Zap, Plus, ArrowDownToLine, Github, Chrome, AlertTriangle
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { subscribeToNativeDashboard, subscribeToActionResults, openNativeDialog, sendDownloadAction, type DownloadCommand, type DashboardSnapshot, type DownloadRow } from "./nativeBridge";
import SettingsPage from "./SettingsPage";
import RageDeskMark from "./RageDeskMark";

type Nav = "all" | "completed" | "queued" | "scheduled" | "analytics" | "category" | "source" | "automation" | "settings";
type Status = "all" | "downloading" | "paused" | "queued" | "completed" | "failed";
type Sort = "queue" | "recent" | "name" | "speed";

const categories: { name: string; icon: LucideIcon; color: string }[] = [
  { name: "All Downloads", icon: Home, color: "yellow" },
  { name: "Programs", icon: Folder, color: "cyan" },
  { name: "Videos", icon: Video, color: "red" },
  { name: "Music", icon: Music2, color: "purple" },
  { name: "Documents", icon: FileText, color: "sky" },
  { name: "Compressed", icon: FileArchive, color: "amber" },
  { name: "Images", icon: ImageIcon, color: "green" },
];
const sites: { name: string; icon: LucideIcon; color: string }[] = [
  { name: "Microsoft", icon: LayoutGrid, color: "cyan" },
  { name: "GitHub", icon: Code2, color: "muted" },
  { name: "Google Drive", icon: HardDrive, color: "green" },
  { name: "YouTube", icon: Video, color: "red" },
];
const statuses: Status[] = ["all", "downloading", "paused", "queued", "completed", "failed"];
const categoryFor = (value: string) => {
  const k = value.toLowerCase();
  if (k === "software" || k === "programs" || k === "applications") return "Programs";
  if (k === "archives" || k === "compressed") return "Compressed";
  if (k === "videos") return "Videos";
  if (k === "music") return "Music";
  if (k === "images") return "Images";
  if (k === "documents") return "Documents";
  return "All Downloads";
};
function IconForFile({ item }: { item: DownloadRow }) {
  const name = item.name.toLowerCase(), cat = categoryFor(item.category);
  if (/githubusercontent\.com$|github\.com$/.test(item.host.toLowerCase()) && /github/i.test(name)) return <span className="file-mark icon-dark"><Github size={20}/></span>;
  if (/chrome/i.test(name) && /google\.com$/.test(item.host.toLowerCase())) return <span className="file-mark icon-chrome"><Chrome size={20}/></span>;
  if (/^googledrive/i.test(name) && /google\.com$/.test(item.host.toLowerCase())) return <span className="file-mark icon-drive"><svg viewBox="0 0 32 32" width="22" height="22" aria-label="Google Drive app"><path fill="#00875a" d="M11 3h8l-11 19H1z"/><path fill="#ffbe00" d="M19 3l12 20h-8L11 3z"/><path fill="#1676ef" d="M8 22h23l-4 7H4z"/></svg></span>;
  if (/docker/i.test(name) && /(^|\.)docker\.com$/.test(item.host.toLowerCase())) return <span className="file-mark icon-docker" title="Docker installer"><svg viewBox="0 0 32 32" width="22" height="22" aria-hidden="true"><path fill="#1eaff1" d="M3 17h4v-4h4v4h3v-8h4v8h3V7h4v10h5c-1 8-8 12-15 12C8 29 4 25 3 17Z"/><path fill="#73dcff" d="M10 13h3v3h-3zm5 0h3v3h-3zm5 0h3v3h-3z"/></svg></span>;
  if (/winrar/i.test(name) && /(^|\.)win-rar\.com$/.test(item.host.toLowerCase())) return <span className="file-mark icon-winrar" title="WinRAR installer"><svg viewBox="0 0 32 32" width="22" height="22" aria-hidden="true"><rect x="4" y="3" width="24" height="9" rx="2" fill="#9c44d5"/><rect x="4" y="12" width="24" height="9" rx="2" fill="#287dc4"/><rect x="4" y="21" width="24" height="8" rx="2" fill="#3fa356"/><rect x="15" y="5" width="5" height="22" rx="1" fill="#d4ad56"/></svg></span>;
  if (/(vscode|codeusersetup)/i.test(name) && /(^|\.)visualstudio\.com$/.test(item.host.toLowerCase())) return <span className="file-mark icon-vscode" title="VS Code installer"><svg viewBox="0 0 32 32" width="22" height="22" aria-hidden="true"><path fill="#1689df" d="m23 3 6 3v20l-6 3-13-11-6 5-3-3 8-8L1 12l3-3 6 5z"/><path fill="#61c7ff" d="m23 9-8 7 8 7z"/></svg></span>;
  if (/(windows|win11).*\.iso$/i.test(name) && /(^|\.)microsoft\.com$/.test(item.host.toLowerCase())) return <span className="file-mark icon-windows" title="Windows disk image"><svg viewBox="0 0 32 32" width="22" height="22" aria-hidden="true"><path fill="#16abef" d="M3 6 14 4v11H3zm13-2 13-2v13H16zM3 17h11v11L3 26zm13 0h13v13l-13-2z"/></svg></span>;
  let Icon: LucideIcon = Folder;
  let color = "blue";
  if (/\.zip$|\.7z$|\.rar$|\.tar$|\.gz$|\.iso$/.test(name) || cat === "Compressed") { Icon = Archive; color = "amber"; }
  else if (/\.mp4$|\.mkv$|\.mov$|\.webm$/.test(name) || cat === "Videos") { Icon = Video; color = "purple"; }
  else if (/\.mp3$|\.flac$|\.wav$/.test(name) || cat === "Music") { Icon = Music2; color = "purple"; }
  else if (/\.pdf$|\.docx?$|\.txt$/.test(name) || cat === "Documents") { Icon = FileText; color = "cyan"; }
  else if (/\.png$|\.jpe?g$|\.webp$/.test(name) || cat === "Images") { Icon = FileImage; color = "green"; }
  else if (/\.exe$|\.msi$|\.msix$/.test(name) || cat === "Programs") { Icon = Code2; color = "blue"; }
  return <span className={`file-mark icon-${color}`}><Icon size={18} strokeWidth={1.9}/></span>;
}
function fmtBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return "—";
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const index = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  return `${(bytes / 1024 ** index).toFixed(index <= 1 ? 0 : 1)} ${units[index]}`;
}
const normalizedStatus = (row: DownloadRow) => row.status.toLowerCase() as Status;
function StatusPill({ status }: {status:string}) {
  const v = status.toLowerCase();
  const I = v === "completed" ? CheckCircle2 : v === "failed" ? XCircle : v === "paused" ? Pause : v === "queued" ? Clock3 : Activity;
  return <span className={`status-pill status-${v}`}><I size={12}/>{status}</span>;
}
function CategoryPill({ category }: { category: string }) {
  const normalized = categoryFor(category);
  const kind = normalized === "All Downloads" ? (category || "Other") : normalized;
  return <span className={`category-pill category-${kind.toLowerCase().replaceAll(" ","-")}`}>{kind}</span>;
}
function SegmentedProgress({value}:{value:number|null}) {
  const segments = 12, complete = value===null?0:Math.max(0,Math.min(segments,Math.round(value/100*segments)));
  return <span className="segmented-progress" role="progressbar" aria-valuemin={0} aria-valuemax={100}
    aria-valuenow={value??undefined} aria-label="Download progress">
    {Array.from({length:segments},(_,i)=><i key={i} className={i<complete?"is-filled":""}/>)}
  </span>;
}
const filterIcons: Record<Exclude<Status, "all">, LucideIcon> = {
  downloading: Download, paused: Pause, queued: Clock3, completed: CheckCircle2, failed: XCircle,
};
function MetricCard({ title, value, note, icon: Icon, chart, variant }: {
  title: string; value: string | number; note: string; icon: LucideIcon; chart: string; variant?: string
}) {
  const points = chart === "line" ? "0,38 10,40 20,30 30,33 40,25 50,36 60,19 70,22 80,12 90,15 100,5 110,11 120,4" : "0,40 10,36 20,40 30,21 40,25 50,17 60,23 70,10 80,16 90,7 100,11 110,2 120,5";
  return <div className="metric-card">
    <div className={`metric-icon ${variant ?? ""}`}><Icon size={25}/></div>
    <div className="metric-copy"><div className="metric-label">{title}</div><div className="metric-value">{value}</div><div className="metric-note">{note}</div></div>
    <svg className="metric-spark" viewBox="0 0 120 45" aria-hidden="true"><polyline points={points} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/></svg>
  </div>;
}
function App() {
  const [data, setData] = useState<DashboardSnapshot | null>(null);
  const [connection, setConnection] = useState<"connecting" | "connected" | "unavailable">("connecting");
  const [nav, setNav] = useState<Nav>("all");
  const [filter, setFilter] = useState<Status>("all");
  const [search, setSearch] = useState("");
  const [sort, setSort] = useState<Sort>("queue");
  const [expanded, setExpanded] = useState({categories: true, sites: true});
  const [selectedCategory, setSelectedCategory] = useState("All Downloads");
  const [selectedSite, setSelectedSite] = useState<string | null>(null);
  const [selectedRow, setSelectedRow] = useState<string | null>(null);
  const [collapsed, setCollapsed] = useState(false);
  const [cardView, setCardView] = useState(false);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(() => new Set());
  const [actionMessage, setActionMessage] = useState("");
  const [rowMenu, setRowMenu] = useState<string | null>(null);
  const act = (action: DownloadCommand, ids: string[]) => {
    if (!sendDownloadAction(action, ids)) setActionMessage("Native RageDesk connection unavailable.");
    else setRowMenu(null);
  };
  const toggleSelect = (id:string) => setSelectedIds(previous=>{const next=new Set(previous);if(next.has(id))next.delete(id);else next.add(id);return next;});
  useEffect(() => subscribeToNativeDashboard(setData, setConnection), []);
  useEffect(() => subscribeToActionResults(setActionMessage), []);
  const rows = data?.rows ?? [];
  useEffect(() => {
    if (!data) return;
    const valid = new Set(data.rows.map(r=>r.id));
    setSelectedIds(previous => [...previous].every(id=>valid.has(id)) ? previous :
      new Set([...previous].filter(id=>valid.has(id))));
  }, [data]);
  const counts = data?.counts;
  const filtered = useMemo(() => {
    let next = rows.filter(r => {
      const status = normalizedStatus(r);
      if (nav === "completed" && status !== "completed") return false;
      if (nav === "queued" && status !== "queued") return false;
      if (nav === "category" && selectedCategory !== "All Downloads" && categoryFor(r.category) !== selectedCategory) return false;
      if (nav === "source" && selectedSite !== null && !matchesSource(r.host, selectedSite)) return false;
      if (filter !== "all" && status !== filter) return false;
      const term = search.trim().toLowerCase();
      return !term || r.name.toLowerCase().includes(term) || r.host.toLowerCase().includes(term);
    });
    if (sort === "recent") next = [...next].sort((a,b) => b.addedAt.localeCompare(a.addedAt));
    if (sort === "name") next = [...next].sort((a,b) => a.name.localeCompare(b.name));
    if (sort === "speed") next = [...next].sort((a,b) => b.bytesPerSecond - a.bytesPerSecond);
    return next;
  }, [rows, nav, selectedCategory, selectedSite, search, sort, filter]);
  const displayRows = filtered.slice(0, 500);
  const allSelected = displayRows.length>0 && displayRows.every(row=>selectedIds.has(row.id));
  const targetIds = selectedIds.size ? [...selectedIds] : selectedRow ? [selectedRow] : [];
  const actionEligible = connection==="connected" && targetIds.length>0;
  const selected = rows.find(r => r.id === selectedRow) ?? null;
  const switchNav = (next: Nav) => { setNav(next); setFilter(next==="queued"?"queued":"all"); setSearch(""); };
  const theme = data?.theme === "Arctic" ? "light" : "dark";
  return <div className={`desktop theme-${theme} ${collapsed ? "sidebar-collapsed" : ""} ${nav === "settings" ? "settings-open" : ""}`}>
    <aside className="sidebar">
      <div className="brand-line"><RageDeskMark/><span className="brand-name">RageDesk</span>
        <button className="icon-button brand-toggle" title="Toggle sidebar" aria-label="Toggle sidebar" onClick={() => setCollapsed(!collapsed)}><Menu size={18}/></button></div>
      <nav aria-label="Main navigation">
        <NavButton label="Downloads" icon={Download} active={nav === "all"} count={counts?.all} onClick={() => switchNav("all")}/>
        <NavButton label="Completed" icon={CheckCircle2} active={nav === "completed"} count={counts?.completed} onClick={() => switchNav("completed")}/>
        <NavButton label="Queue" icon={List} active={nav === "queued"} count={counts?.queued} onClick={() => switchNav("queued")}/>
        <NavButton label="Scheduler" icon={CalendarClock} active={nav === "scheduled"} count={data?.scheduledCount} onClick={() => switchNav("scheduled")}/>
        <NavButton label="Analytics" icon={BarChart3} active={nav === "analytics"} onClick={() => document.querySelector(".bottom-panels")?.scrollIntoView({behavior:"smooth"})}/>
        <div className="nav-section">
          <button className="nav-section-title" onClick={() => setExpanded(v => ({...v,categories:!v.categories}))} aria-expanded={expanded.categories}><span>Categories</span>{expanded.categories ? <ChevronDown size={16}/> : <ChevronDown size={16} className="rotate"/>}</button>
          {expanded.categories && categories.map(({name,icon,color}) => <NavButton key={name} label={name} icon={icon} color={color}
            count={data?.categoryCounts[name] ?? 0}
            active={nav === "category" && selectedCategory === name}
            onClick={() => { setSelectedCategory(name); switchNav("category"); }}/>)}
        </div>
        <section className="browser-extension-card" aria-label="Browser extension installation">
          <div className="extension-art"><MonitorDown size={29}/><span><Package size={22}/></span></div>
          <div className="extension-heading">Install RageDesk<br/>Browser Extension</div>
          <p>Capture links instantly<br/>from your browser.</p>
          <button type="button" onClick={()=>openNativeDialog("ui.openSettings")} title="Open existing WPF Browser Capture settings">
            Install Extension
          </button>
        </section>
        <div className="nav-section">
          <button className="nav-section-title" onClick={() => setExpanded(v => ({...v,sites:!v.sites}))} aria-expanded={expanded.sites}><span>Sites / Groups</span>{expanded.sites ? <ChevronDown size={16}/> : <ChevronDown size={16} className="rotate"/>}</button>
          {expanded.sites && sites.map(({name,icon,color}) => <NavButton key={name} label={name} icon={icon} color={color} count={data?.siteCounts[name] ?? 0}
            active={nav === "source" && selectedSite === name} onClick={() => {setSelectedSite(name);switchNav("source");}}/>)}
          {expanded.sites && <span className="nav-subnote">+ Add preset in WPF settings</span>}
        </div>
      </nav>
      <div className="sidebar-bottom">
        <NavButton label="Automation" icon={Zap} onClick={() => openNativeDialog("ui.openSettings")}/>
        <NavButton label="Settings" icon={Settings2} active={nav==="settings"} onClick={() => switchNav("settings")}/>
        <div className="local-mode"><ShieldCheck size={14}/><span>Local only</span></div>
      </div>
    </aside>

    <main className="workspace">
      <header className="toolbar">
        <div className="global-search"><Search size={18}/><input aria-label="Search downloads" value={search} onChange={e=>setSearch(e.target.value)} placeholder="Search downloads, files, or paste links…"/><kbd>Ctrl + L</kbd></div>
        <div className="toolbar-actions">
          <button className="toolbar-button" onClick={()=>openNativeDialog("ui.openAddDownload")} title="Opens the existing native Add Download dialog"><ExternalLink size={17}/> Add Link</button>
          <button className="toolbar-button action-primary" onClick={()=>openNativeDialog("ui.openAddDownload")} title="Opens the existing native Add Download dialog"><Plus size={17}/> New Download <ChevronDown size={15}/></button>
          <button className="square-button" disabled={!actionEligible} title="Resume selected downloads" onClick={()=>act("resume",targetIds)}><Play size={17}/></button>
          <button className="square-button" disabled={!actionEligible} title="Pause selected downloads" onClick={()=>act("pause",targetIds)}><Pause size={17}/></button>
          <span className="top-local"><CloudDownload size={17}/> Local only</span>
          <button className="square-button" aria-label="Open settings" onClick={()=>switchNav("settings")}><Settings2 size={18}/></button>
        </div>
      </header>

      {nav==="settings" ? <SettingsPage data={data} onBack={()=>switchNav("all")}/> : <div className="content-pane">
        <div className="metrics-grid">
          <MetricCard title="ACTIVE DOWNLOADS" icon={Download} value={counts?.downloading ?? "—"} note="Active transfers" chart="line"/>
          <MetricCard title="CURRENT SPEED" icon={Activity} value={data?.summary.currentSpeed ?? "—"} note="Measured network speed" chart="bars"/>
          <MetricCard title="DOWNLOADED TODAY" icon={HardDrive} value={data?.summary.downloadedToday ?? "—"} note="Known completions today" chart="bars"/>
          <MetricCard title="TIME REMAINING" icon={Clock3} value={data?.summary.timeRemaining ?? "—"} note="Estimated active duration" chart="line" variant="purple"/>
        </div>
        <div className="list-controls">
          <div className="status-tabs" role="tablist" aria-label="Download status">
            {statuses.map(s => <button role="tab" aria-selected={filter===s} key={s} onClick={()=>setFilter(s)} className={`tab-button ${filter===s?"is-active":""}`}>
              {s!=="all"&&(() => { const Icon=filterIcons[s]; return <Icon size={15} strokeWidth={2.7}/>; })()}
              {s==="all"?"All":s[0].toUpperCase()+s.slice(1)} <span className="tab-count">{counts?.[s] ?? 0}</span>
            </button>)}
          </div>
          <div className="filter-tools"><button className="filter-button" title="Filter by the status tabs above"><Filter size={16}/> Filter</button>
            <select aria-label="Sort downloads" value={sort} onChange={e=>setSort(e.target.value as Sort)}>
              <option value="queue">Queue order</option><option value="recent">Added (newest)</option>
              <option value="name">Name A–Z</option><option value="speed">Speed (highest)</option>
            </select>
            <button className={`square-button ${!cardView?"is-selected":""}`} aria-label="Table view" onClick={()=>setCardView(false)}><List size={18}/></button>
            <button className={`square-button ${cardView?"is-selected":""}`} aria-label="Card view" onClick={()=>setCardView(true)}><LayoutGrid size={18}/></button>
          </div>
        </div>
        <section className="downloads-panel" aria-label="Download list">
          <div className="selection-toolbar">
            <label className="selection-total"><input type="checkbox" checked={allSelected} onChange={()=>setSelectedIds(allSelected?new Set():new Set(displayRows.map(row=>row.id)))}/>
              {selectedIds.size} selected</label>
            <button disabled={!selectedIds.size} onClick={()=>act("removeHistory",targetIds)} title="Remove only completed, failed or canceled entries from history (files are kept)"><Trash2 size={15}/> Remove History</button>
            <button disabled={selectedIds.size!==1} onClick={()=>act("openFolder",targetIds)}><FolderOpen size={15}/> Open Folder</button>
            <button disabled={!selectedIds.size} onClick={()=>act("resume",targetIds)}><Play size={16}/> Start / Resume</button>
            <button disabled={!selectedIds.size} onClick={()=>act("pause",targetIds)}><Pause size={15}/> Pause</button>
            <button disabled={!selectedIds.size} onClick={()=>act("stop",targetIds)}><AlertTriangle size={15}/> Stop</button>
          </div>
          <div className="table-scroll">
            {!cardView ? <table className="download-table"><thead><tr><th aria-label="Select all visible downloads" className="check-col">
                <input type="checkbox" checked={allSelected} onChange={()=>setSelectedIds(allSelected?new Set():new Set(displayRows.map(row=>row.id)))}/>
              </th><th className="row-index-col">#</th><th className="name-col">Name</th><th>Category</th><th>Status</th>
              <th>Progress</th><th>Size</th><th>Speed</th><th>ETA</th><th>Connections</th><th>Added</th><th aria-label="Actions"/></tr></thead>
              <tbody>{displayRows.map((r,index)=> <tr key={r.id} className={`download-row ${selectedIds.has(r.id)?"selected-row":""}`} onClick={()=>setSelectedRow(r.id)} onDoubleClick={()=>act("details",[r.id])} onContextMenu={e=>{e.preventDefault();setSelectedRow(r.id);setRowMenu(r.id);}}>
                <td><input type="checkbox" aria-label={`Select ${r.name}`} checked={selectedIds.has(r.id)} onClick={e=>e.stopPropagation()} onChange={()=>toggleSelect(r.id)}/></td>
                <td className="row-index">{index+1}</td>
                <td><div className="filename"><IconForFile item={r}/><div className="file-lines"><strong title={r.name}>{r.name}</strong><span title={r.host}>{r.host}</span></div></div></td>
                <td><CategoryPill category={r.category}/></td><td><StatusPill status={r.status}/></td>
                <td><div className="progress-cell"><SegmentedProgress value={r.percent}/><span>{r.percent===null?"—":`${r.percent.toFixed(1)}%`}</span></div></td>
                <td className="numbers">{r.totalBytes === null ? fmtBytes(r.receivedBytes) : `${fmtBytes(r.receivedBytes)} / ${fmtBytes(r.totalBytes)}`}</td>
                <td className="numbers">{r.status.toLowerCase()==="downloading" && r.bytesPerSecond>0 ? fmtBytes(r.bytesPerSecond)+"/s":"—"}</td><td>{r.eta}</td>
                <td><span className="connections"><Activity size={12}/>{r.connections} / {r.requestedConnections}</span></td>
                <td className="added">{r.added}</td><td><div className="row-actions">
                  <button aria-label={`Open folder for ${r.name}`} onClick={e=>{e.stopPropagation();act("openFolder",[r.id]);}}><Folder size={16}/></button>
                  <button title="Remove history entry (keeps downloaded files)" onClick={e=>{e.stopPropagation();act("removeHistory",[r.id]);}}><Trash2 size={16}/></button>
                  <button aria-label={`More actions for ${r.name}`} onClick={e=>{e.stopPropagation();setRowMenu(rowMenu===r.id?null:r.id);}}><MoreHorizontal size={17}/></button>
                  {rowMenu===r.id&&<div className="react-row-menu" onClick={e=>e.stopPropagation()}>
                    <button onClick={()=>act("details",[r.id])}>View transfer details</button>
                    <button onClick={()=>act("resume",[r.id])}>Start / Resume</button>
                    <button onClick={()=>act("pause",[r.id])}>Pause</button>
                    <button onClick={()=>act("stop",[r.id])}>Stop (confirm)</button>
                    <button onClick={()=>act("schedule",[r.id])}>Schedule start</button>
                    <button onClick={()=>act("earlier",[r.id])}>Move earlier</button>
                    <button onClick={()=>act("later",[r.id])}>Move later</button>
                    <button onClick={()=>act("openFolder",[r.id])}>Open folder</button>
                    <button onClick={()=>act("openFile",[r.id])}>Open completed file</button>
                    <button onClick={()=>setRowMenu(null)}>Close menu</button>
                  </div>}

                </div></td>
              </tr>)}</tbody></table> :
              <div className="download-cards">{displayRows.map(r=><button key={r.id} className="download-mini-card" onClick={()=>setSelectedRow(r.id)}><IconForFile item={r}/><strong title={r.name}>{r.name}</strong><StatusPill status={r.status}/><div className="progress-track"><span style={{width:`${r.percent??0}%`}}/></div></button>)}</div>}
            {displayRows.length === 0 && <div className="empty-list"><FolderOpen size={28}/><strong>{connection==="connected"?"No downloads match this view":"Waiting for RageDesk data"}</strong><span>{connection==="connected"?"Adjust your filters or search.":"Open the desktop preview using --react-preview. No demo transactions are shown."}</span></div>}
          </div>
        </section>
        <section className="bottom-panels" aria-label="Download insights">
          <div className="insight-panel">
            <div className="insight-heading"><span><Activity size={16}/> Network &amp; Performance · selected download</span><span className="subtle-pill">Real-time</span></div>
            <div className="network-body"><div className="network-graph">{data?.speedSamples?.length ? <SpeedGraph values={data.speedSamples}/> :
              <span className="graph-empty">Speed graph starts when you download</span>}</div>
              <div className="network-readouts"><span><ArrowDownToLine size={17}/> <b>{data?.summary.currentSpeed ?? "—"}</b><small>Download</small></span>
                <span><Zap size={17}/> <b>{selected?.connections ?? 0}</b><small>Connections</small></span>
              </div></div>
          </div>
          <div className="insight-panel"><div className="insight-heading"><span><Globe2 size={16}/> Top Sources</span><span className="subtle-pill">All time</span></div>
            <div className="source-list">{(data?.sources??[]).map(s=><div className="source-item" key={s.host}>
              <Globe2 size={15}/><span title={s.host}>{s.host}</span><span className="source-bytes">{fmtBytes(s.bytes)}</span>
              <div className="source-bar"><span style={{width:`${data?.sources?.[0]?.bytes ? Math.max(2,100*s.bytes/data.sources[0].bytes):0}%`}}/></div></div>)}
              {!data?.sources?.length && <span className="hint">No source data available.</span>}</div>
          </div>
          <div className="insight-panel"><div className="insight-heading"><span><Clock3 size={16}/> Recent Activities</span><span className="linkish">See All ›</span></div>
            <div className="activity-list">{(data?.recent??[]).map(e=><div className="activity-item" key={e.id}><span className="activity-dot"/><time>{e.added}</time><span title={e.description}>{e.description}</span></div>)}
              {!data?.recent?.length && <span className="hint">No activity recorded.</span>}</div>
          </div>
        </section>
        {actionMessage&&<div className="native-action-feedback" role="status"><Check size={15}/>{actionMessage}<button aria-label="Dismiss message" onClick={()=>setActionMessage("")}>×</button></div>}
        <div className="preview-footer"><span className="footer-ready"><span className="status-green-dot"/>{connection==="connected"?"Ready":connection==="unavailable"?"Preview unavailable":"Connecting"}</span>
          <span><CloudDownload size={15}/> Sync coming soon</span>
          <span><HardDrive size={15}/> {data?.storage ? `${fmtBytes(data.storage.freeBytes)} free`:"Disk space unavailable"}</span>
          <span><Check size={14}/> {counts?.failed ? `${counts.failed} failed downloads`:"No failed downloads"}</span></div>
      </div>}
    </main>
  </div>;
}
function NavButton({label, icon: Icon, count, onClick, active=false, disabled=false, color="blue"}:{
  label:string; icon:LucideIcon; count?:number; onClick:()=>void; active?:boolean; disabled?:boolean;color?:string
}) {return <button className={`nav-item ${active?"is-active":""} ${disabled?"coming-soon":""}`}
    onClick={onClick} disabled={disabled} title={disabled?`${label} remains available in WPF during the migration preview`:label}>
    <Icon size={19} strokeWidth={2} className={`nav-icon color-${color}`}/><span className="nav-text">{label}</span>
    {count!==undefined&&<span className="nav-badge">{count}</span>}</button>}
function matchesSource(host:string,source:string){const h=host.toLowerCase();if(source==="Microsoft")return /microsoft\.com|windows\.net|msedge\.net/.test(h);if(source==="GitHub")return /github\.com|githubusercontent\.com/.test(h);if(source==="Google Drive")return /google\.com|googleusercontent\.com/.test(h);if(source==="YouTube")return /youtube\.com|googlevideo\.com/.test(h);return false;}
function SpeedGraph({values}:{values:number[]}) {
  const bounded = values.slice(-60).map(v=>Number.isFinite(v)?Math.max(0,v):0);
  const peak=Math.max(1,...bounded);
  const points=bounded.map((v,i)=>`${bounded.length===1?0:i/(bounded.length-1)*100},${100-((v/peak)*85)}`).join(" ");
  return <svg className="speed-chart" viewBox="0 0 100 100" preserveAspectRatio="none" aria-label="Real measured download speed history">
    <defs><linearGradient id="speedFill" x1="0" y1="0" x2="0" y2="1"><stop stopColor="#1689ff" stopOpacity=".5"/><stop offset="1" stopColor="#1689ff" stopOpacity="0"/></linearGradient></defs>
    {[20,45,70,95].map(n=><line key={n} x1="0" y1={n} x2="100" y2={n} stroke="#1b456f" strokeWidth=".5"/>)}
    <polygon points={`0,100 ${points} 100,100`} fill="url(#speedFill)"/><polyline points={points} fill="none" stroke="#23c5ff" strokeWidth="1.4" vectorEffect="non-scaling-stroke"/>
  </svg>;
}
export default App;
