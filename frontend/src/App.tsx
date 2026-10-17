import { useEffect, useMemo, useState } from "react";
import {
  Activity, Archive, BarChart3, CalendarClock, Check, CheckCircle2,
  ChevronDown, Clock3, CloudDownload, Code2, Download, ExternalLink,
  FileArchive, FileImage, FileText, Filter, Folder, FolderOpen,
  Globe2, HardDrive, Image as ImageIcon, LayoutGrid, List, Menu,
  MoreHorizontal, Music2, Pause, Play, Search, Settings2,
  ShieldCheck, Video, XCircle,
  Zap, CircleAlert, Plus, ArrowDownToLine
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import { subscribeToNativeDashboard, type DashboardSnapshot, type DownloadRow } from "./nativeBridge";

type Nav = "all" | "completed" | "queued" | "scheduled" | "analytics" | "category" | "source" | "automation" | "settings";
type Status = "all" | "downloading" | "paused" | "queued" | "completed" | "failed";
type Sort = "queue" | "recent" | "name" | "speed";

const categories: { name: string; icon: LucideIcon; color: string }[] = [
  { name: "All Downloads", icon: FolderOpen, color: "blue" },
  { name: "Programs", icon: Code2, color: "blue" },
  { name: "Videos", icon: Video, color: "cyan" },
  { name: "Music", icon: Music2, color: "purple" },
  { name: "Documents", icon: FileText, color: "blue" },
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
function RageDeskMark() {
  return <svg className="brand-mark" role="img" aria-label="RageDesk logo" viewBox="0 0 64 68" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <linearGradient id="logoBack" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#137BFF"/><stop offset="1" stopColor="#092A9E"/></linearGradient>
      <linearGradient id="logoFront" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#42E9FF"/><stop offset="1" stopColor="#157BFF"/></linearGradient>
      <linearGradient id="logoCore" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#23D5FF"/><stop offset="1" stopColor="#0960EF"/></linearGradient>
    </defs>
    <path d="M4 14 27 2 57 16 57 29 46 35 46 23 27 14 15 20Z" fill="url(#logoBack)"/>
    <path d="M4 14 15 20 15 42 29 35 45 46 61 65 40 65 24 48 15 53 15 65 4 65Z" fill="url(#logoCore)"/>
    <path d="M15 28 46 15 57 21 57 29 26 46 15 42 15 34 45 23 27 14Z" fill="url(#logoFront)"/>
  </svg>;
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
  useEffect(() => subscribeToNativeDashboard(setData, setConnection), []);
  const rows = data?.rows ?? [];
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
  const selected = rows.find(r => r.id === selectedRow) ?? null;
  const switchNav = (next: Nav) => { setNav(next); setFilter("all"); setSearch(""); };
  const theme = data?.theme === "Arctic" ? "light" : "dark";
  return <div className={`desktop theme-${theme} ${collapsed ? "sidebar-collapsed" : ""}`}>
    <aside className="sidebar">
      <div className="brand-line"><RageDeskMark/><span className="brand-name">RageDesk</span>
        <button className="icon-button brand-toggle" title="Toggle sidebar" aria-label="Toggle sidebar" onClick={() => setCollapsed(!collapsed)}><Menu size={18}/></button></div>
      <nav aria-label="Main navigation">
        <NavButton label="Downloads" icon={Download} active={nav === "all"} count={counts?.all} onClick={() => switchNav("all")}/>
        <NavButton label="Completed" icon={CheckCircle2} active={nav === "completed"} count={counts?.completed} onClick={() => switchNav("completed")}/>
        <NavButton label="Queue" icon={List} active={nav === "queued"} count={counts?.queued} onClick={() => switchNav("queued")}/>
        <NavButton label="Scheduler" icon={CalendarClock} active={nav === "scheduled"} count={data?.scheduledCount} disabled onClick={() => switchNav("scheduled")}/>
        <NavButton label="Analytics" icon={BarChart3} active={nav === "analytics"} onClick={() => document.querySelector(".bottom-panels")?.scrollIntoView({behavior:"smooth"})}/>
        <div className="nav-section">
          <button className="nav-section-title" onClick={() => setExpanded(v => ({...v,categories:!v.categories}))} aria-expanded={expanded.categories}><span>Categories</span>{expanded.categories ? <ChevronDown size={16}/> : <ChevronDown size={16} className="rotate"/>}</button>
          {expanded.categories && categories.map(({name,icon,color}) => <NavButton key={name} label={name} icon={icon} color={color}
            count={data?.categoryCounts[name] ?? 0}
            active={nav === "category" && selectedCategory === name}
            onClick={() => { setSelectedCategory(name); switchNav("category"); }}/>)}
        </div>
        <div className="nav-section">
          <button className="nav-section-title" onClick={() => setExpanded(v => ({...v,sites:!v.sites}))} aria-expanded={expanded.sites}><span>Sites / Groups</span>{expanded.sites ? <ChevronDown size={16}/> : <ChevronDown size={16} className="rotate"/>}</button>
          {expanded.sites && sites.map(({name,icon,color}) => <NavButton key={name} label={name} icon={icon} color={color} count={data?.siteCounts[name] ?? 0}
            active={nav === "source" && selectedSite === name} onClick={() => {setSelectedSite(name);switchNav("source");}}/>)}
          {expanded.sites && <span className="nav-subnote">+ Add preset in WPF settings</span>}
        </div>
      </nav>
      <div className="sidebar-bottom">
        <NavButton label="Automation" icon={Zap} disabled onClick={() => switchNav("automation")}/>
        <NavButton label="Settings" icon={Settings2} disabled onClick={() => switchNav("settings")}/>
        <div className="local-mode"><ShieldCheck size={14}/><span>Local only · preview</span></div>
      </div>
    </aside>

    <main className="workspace">
      <header className="toolbar">
        <div className="global-search"><Search size={18}/><input aria-label="Search downloads" value={search} onChange={e=>setSearch(e.target.value)} placeholder="Search downloads, files, or paste links…"/><kbd>Ctrl + L</kbd></div>
        <div className="toolbar-actions">
          <button className="toolbar-button" disabled title="Add Link is available in the WPF version during preview"><ExternalLink size={17}/> Add Link</button>
          <button className="toolbar-button action-primary" disabled title="New Download remains in the WPF version during preview"><Plus size={17}/> New Download <ChevronDown size={15}/></button>
          <button className="square-button" disabled title="Available in WPF"><Play size={17}/></button>
          <button className="square-button" disabled title="Available in WPF"><Pause size={17}/></button>
          <span className="top-local"><CloudDownload size={17}/> Local only</span>
          <button className="square-button" disabled aria-label="Settings in WPF" title="Settings remain in WPF"><Settings2 size={18}/></button>
        </div>
      </header>

      <div className="content-pane">
        <div className="metrics-grid">
          <MetricCard title="ACTIVE DOWNLOADS" icon={Download} value={counts?.downloading ?? "—"} note="Active transfers" chart="line"/>
          <MetricCard title="CURRENT SPEED" icon={Activity} value={data?.summary.currentSpeed ?? "—"} note="Measured network speed" chart="bars"/>
          <MetricCard title="DOWNLOADED TODAY" icon={HardDrive} value={data?.summary.downloadedToday ?? "—"} note="Known completions today" chart="bars"/>
          <MetricCard title="TIME REMAINING" icon={Clock3} value={data?.summary.timeRemaining ?? "—"} note="Estimated active duration" chart="line" variant="purple"/>
        </div>
        <div className="list-controls">
          <div className="status-tabs" role="tablist" aria-label="Download status">
            {statuses.map(s => <button role="tab" aria-selected={filter===s} key={s} onClick={()=>setFilter(s)} className={`tab-button ${filter===s?"is-active":""}`}>
              {s!=="all"&&<span className={`status-dot dot-${s}`}/>}
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
          <div className="table-scroll">
            {!cardView ? <table className="download-table"><thead><tr><th aria-label="Selection" className="check-col"><span className="fake-check"/></th>
              <th className="name-col">Name</th><th>Status</th><th>Progress</th><th>Size</th><th>Speed</th><th>ETA</th><th>Connections</th><th>Added</th><th aria-label="Actions"/></tr></thead>
              <tbody>{displayRows.map(r=> <tr key={r.id} className={`download-row ${selected?.id===r.id?"selected-row":""}`} onClick={()=>setSelectedRow(r.id)}>
                <td><span className={`fake-check ${selected?.id===r.id?"checked":""}`}/></td><td><div className="filename"><IconForFile item={r}/><div className="file-lines"><strong title={r.name}>{r.name}</strong><span title={r.host}>{r.host}</span></div></div></td>
                <td><StatusPill status={r.status}/></td><td><div className="progress-cell"><div className="progress-track"><span style={{width:`${Math.min(100,Math.max(0,r.percent ?? 0))}%`}}/></div><span>{r.percent === null ? "—" : `${r.percent.toFixed(1)}%`}</span></div></td>
                <td className="numbers">{r.totalBytes === null ? fmtBytes(r.receivedBytes) : `${fmtBytes(r.receivedBytes)} / ${fmtBytes(r.totalBytes)}`}</td>
                <td className="numbers">{r.status.toLowerCase()==="downloading" && r.bytesPerSecond>0 ? fmtBytes(r.bytesPerSecond)+"/s":"—"}</td><td>{r.eta}</td>
                <td><span className="connections"><Activity size={12}/>{r.connections} / {r.requestedConnections}</span></td>
                <td className="added">{r.added}</td><td><MoreHorizontal size={17} className="muted"/></td>
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
        <div className="preview-footer">{connection==="connected"?<><Check size={14}/> Live C# snapshot · Read-only React preview</>:<><CircleAlert size={14}/> {connection==="unavailable"?"WebView2 native bridge not available":"Connecting to RageDesk..."}</>}
          <span>Changes to files and downloads remain in WPF during migration.</span></div>
      </div>
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
