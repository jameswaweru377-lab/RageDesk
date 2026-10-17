import { useEffect, useState, type ReactNode } from "react";
import RageDeskMark from "./RageDeskMark";
import {
  Bell, CheckCircle2, ChevronDown, CloudDownload, Download, ExternalLink,
  Eye, Folder, Globe2, HardDrive, LayoutList, Palette, Power, RefreshCcw,
  Save, Settings2, Shield, SlidersHorizontal, Zap, X, Wrench
} from "lucide-react";
import { openNativeDialog, sendBrowserAction, sendPreference, chooseDownloadFolder, subscribeToActionResults, type DashboardSnapshot } from "./nativeBridge";

type Tab = "General" | "Downloads" | "Browser Capture" | "Connections" | "Appearance" | "Advanced";
const tabs = [
  { name: "General", subtitle: "Basic settings & behavior", icon: Settings2 },
  { name: "Downloads", subtitle: "Download preferences", icon: Download },
  { name: "Browser Capture", subtitle: "Capture from browsers", icon: Globe2 },
  { name: "Connections", subtitle: "Network & proxy settings", icon: ShareNetworkIcon },
  { name: "Appearance", subtitle: "Theme, colors & UI", icon: Palette },
  { name: "Advanced", subtitle: "Advanced options", icon: SlidersHorizontal },
] as const;

function ShareNetworkIcon({size = 24}: {size?: number}) {
  return <Globe2 size={size} strokeWidth={1.8}/>;
}
function Switch({ checked, title, onChange }: { checked: boolean; title: string; onChange?: () => void }) {
  return <button type="button" disabled={!onChange} className={`settings-switch ${checked ? "on" : ""}`} role="switch" aria-checked={checked} aria-label={title} onClick={onChange}/>;
}
function SettingRow({ icon: Icon, title, description, children }: {
  icon: typeof Download; title: string; description?: string; children?: ReactNode
}) {
  return <div className="setting-row"><Icon size={23} className="setting-row-icon"/>
    <div className="setting-row-copy"><strong>{title}</strong>{description && <span>{description}</span>}</div>
    <div className="setting-row-control">{children}</div>
  </div>;
}
function Panel({ icon: Icon, title, subtitle, children, className="" }: {
  icon: typeof Download; title: string; subtitle?: string; children: ReactNode; className?: string
}) {
  return <section className={`settings-panel ${className}`}>
    <div className="settings-panel-heading"><Icon size={25}/><div><strong>{title}</strong>{subtitle && <span>{subtitle}</span>}</div></div>
    <div className="settings-panel-body">{children}</div>
  </section>;
}
export default function SettingsPage({ data, onBack, initialTab="General" }: { data: DashboardSnapshot | null; onBack: () => void; initialTab?: Tab }) {
  const [tab, setTab] = useState<Tab>(initialTab);
  const [nativeResult, setNativeResult] = useState("");
  useEffect(() => subscribeToActionResults(setNativeResult), []);
  const actual = data?.settings;
  const accent = data?.theme === "Arctic" ? "Arctic Light" : "Cobalt Dark";
  const setNative = (key: "startImmediately" | "theme" | "defaultConnections" | "maxConcurrentDownloads" | "bandwidthLimit", value: boolean | string | number) => {
    if (!sendPreference(key,value)) setNativeResult("RageDesk native settings are unavailable.");
  };
  return <div className="settings-page">
    <header className="settings-hero"><div className="settings-brand"><RageDeskMark/><strong>RageDesk</strong></div><div className="settings-hero-mark"><Settings2 size={31}/></div>
      <div className="settings-hero-copy"><h1>Settings</h1><p>Customize your experience and manage application preferences</p></div>
      <button className="settings-back" onClick={onBack} aria-label="Back to downloads"><X size={20}/></button>
    </header>
    <div className="settings-nav" role="tablist" aria-label="Settings sections">
      {tabs.map(({name,subtitle,icon:Icon})=><button key={name} type="button" role="tab" aria-selected={tab===name}
        className={`settings-nav-item ${tab===name?"active":""}`} onClick={()=>setTab(name)}>
        <Icon size={30}/><span><strong>{name}</strong><small>{subtitle}</small></span></button>)}
    </div>
    {tab==="General" ? <>
      <div className="settings-two-column">
        <Panel className="settings-general" icon={Settings2} title="General Settings" subtitle="Core application behavior and downloads">
          <SettingRow icon={Folder} title="Default download folder" description="The currently configured folder is kept private in this preview">
            <div className="readonly-field">Current local download folder</div><button className="settings-mini-button" onClick={()=>{if(!chooseDownloadFolder())setNativeResult("Native folder picker unavailable.");}} title="Choose default download folder using Windows folder picker">Browse</button>
          </SettingRow>
          <SettingRow icon={Zap} title="Start downloads immediately" description="Automatically start new downloads after adding"><Switch checked={actual?.startImmediately??false} title="Start downloads immediately" onChange={actual?()=>setNative("startImmediately",!actual.startImmediately):undefined}/></SettingRow>
          <SettingRow icon={RefreshCcw} title="Smart retry on failure" description="Retry behavior continues to be managed by the native download engine">
            <span className="settings-pending">Managed by C#</span>
          </SettingRow>
          <SettingRow icon={LayoutList} title="Split into multiple connections" description="Configured default connection count">
            <select className="readonly-count" disabled={!actual} aria-label="Default connections" value={actual?.defaultConnections??8} onChange={e=>setNative("defaultConnections",Number(e.target.value))}>{[1,2,4,8,16,32].map(v=><option key={v} value={v}>{v}</option>)}</select><span className="setting-suffix">connections</span>
          </SettingRow>
        </Panel>
        <Panel className="settings-preview-panel" icon={Eye} title="Application Preview" subtitle="Live appearance of the approved RageDesk theme">
          <div className="app-preview-mini">
            <div className="mini-sidebar"><div className="mini-brand">◈ RageDesk</div><div className="mini-highlight">⌂ &nbsp; Downloads</div><span>☷ &nbsp; Queue</span><span>◷ &nbsp; Scheduler</span><span>▥ &nbsp; Analytics</span></div>
            <div className="mini-work"><div className="mini-search">⌕ &nbsp; Search downloads</div><div className="mini-download">
              <div className="mini-download-mark"><Download size={20}/></div><div><strong>RageDesk design preview</strong><span>Responsive offline interface</span><div className="mini-progress"><span/></div></div><b>50%</b>
            </div><div className="mini-ghost"/><div className="mini-ghost second"/></div>
          </div>
          <div className="preview-colors"><span>Theme Colors</span><i className="selected"/><i className="sky"/><i className="violet"/><i className="amber"/><i className="green"/><span className="theme-current">{accent}</span></div>
          <div className="settings-notice"><Shield size={15}/> Theme changes now save locally. Additional accent colours remain a future option.</div>
        </Panel>
      </div>
      <div className="settings-three-column">
        <Panel icon={Power} title="Startup & Behavior" subtitle="How RageDesk starts and runs">
          <SettingRow icon={Power} title="Launch at Windows startup" description="Not configured in this preview"><span className="settings-pending">WPF</span></SettingRow>
          <SettingRow icon={CloudDownload} title="Start minimized to system tray" description="Current native behavior remains unchanged"><span className="settings-pending">Native</span></SettingRow>
          <SettingRow icon={ChevronDown} title="When closing the app" description="Downloads are controlled by the existing app"><span className="settings-pending">Native</span></SettingRow>
          <SettingRow icon={RefreshCcw} title="Check for updates" description="Not enabled by the React preview"><span className="settings-pending">Coming later</span></SettingRow>
        </Panel>
        <Panel icon={Bell} title="Notifications" subtitle="Events handled by the native desktop app">
          <SettingRow icon={CheckCircle2} title="Download completed" description="Native notification settings are unchanged"><span className="settings-pending">Native</span></SettingRow>
          <SettingRow icon={Bell} title="Download failed" description="No new notification services added"><span className="settings-pending">Native</span></SettingRow>
          <SettingRow icon={LayoutList} title="Queue completed" description="Existing queue remains in C#"><span className="settings-pending">Native</span></SettingRow>
          <SettingRow icon={Globe2} title="Browser capture detected" description="Existing extension flow is preserved"><span className="settings-pending">Native</span></SettingRow>
        </Panel>
        <Panel icon={HardDrive} title="Data Management" subtitle="Local data and application settings">
          <SettingRow icon={Save} title="Export settings" description="Use existing application settings"><button disabled className="settings-mini-button">Export</button></SettingRow>
          <SettingRow icon={Download} title="Import settings" description="No third-party sync or data transfer"><button disabled className="settings-mini-button">Import</button></SettingRow>
          <SettingRow icon={RefreshCcw} title="Reset to default" description="Available only through validated native flow"><button disabled className="settings-mini-button danger">Reset</button></SettingRow>
          <SettingRow icon={Folder} title="Configuration location" description="Stored locally on your Windows device"><span className="settings-pending">Local only</span></SettingRow>
        </Panel>
      </div>
    </> : <div className="settings-section-view">
      <Panel icon={tab==="Appearance"?Palette:tab==="Connections"?Globe2:tab==="Browser Capture"?Globe2:tab==="Downloads"?Download:Wrench}
        title={tab} subtitle="Validated options save to your existing local RageDesk preferences">
        <p>RageDesk saves supported options locally through its existing C# preferences service. Network proxy and folder changes remain in native Settings.</p>
        {tab==="Appearance"&&<><SettingRow icon={Palette} title="Current theme" description="Saved native preference"><select className="readonly-count" value={data?.theme??"Cobalt"} onChange={e=>setNative("theme",e.target.value)}><option value="Cobalt">Cobalt Dark</option><option value="Arctic">Arctic Light</option></select></SettingRow></>}
        {tab==="Downloads"&&<><SettingRow icon={LayoutList} title="Default connections" description="For new downloads"><select className="readonly-count" disabled={!actual} value={actual?.defaultConnections??8} onChange={e=>setNative("defaultConnections",Number(e.target.value))}>{[1,2,4,8,16,32].map(v=><option key={v} value={v}>{v}</option>)}</select></SettingRow><SettingRow icon={Download} title="Simultaneous downloads" description="Changes require restarting RageDesk"><select className="readonly-count" disabled={!actual} value={actual?.maxConcurrentDownloads??2} onChange={e=>setNative("maxConcurrentDownloads",Number(e.target.value))}>{[1,2,3,4].map(v=><option key={v} value={v}>{v}</option>)}</select></SettingRow></>}
        {tab==="Connections"&&<><SettingRow icon={Globe2} title="Proxy enabled" description="Native network settings; proxy address is never passed into the preview"><strong>{actual?.proxyEnabled?"Yes":"No"}</strong></SettingRow><SettingRow icon={SlidersHorizontal} title="Bandwidth limit" description="Native setting (KiB/s; zero means unlimited)"><input className="readonly-count" type="number" min={0} max={1048576} disabled={!actual} aria-label="Default bandwidth limit KiB/s" defaultValue={actual?.defaultBandwidthKiBps??0} onBlur={e=>setNative("bandwidthLimit",Number(e.target.value))}/></SettingRow></>}
        {tab==="Browser Capture"&&<>
          <SettingRow icon={Shield} title="RageDesk Browser Capture"
            description="Chrome: first load the bundled extension manually, then register the local native messaging host. Browser permissions and automatic capture remain opt-in.">
            <button className="settings-mini-button" onClick={()=>sendBrowserAction("extensionsPage")}>Open Chrome extensions</button>
            <button className="settings-mini-button" onClick={()=>sendBrowserAction("extensionFolder")}>Open extension folder</button>
          </SettingRow>
          <SettingRow icon={Globe2} title="Native host connection"
            description="Register for this Windows account only. Check the extension's connection badge in Chrome to test the full handoff.">
            <button className="settings-mini-button" onClick={()=>sendBrowserAction("connect")}>Connect Chrome</button>
            <button className="settings-mini-button" onClick={()=>sendBrowserAction("check")}>Check registration</button>
          </SettingRow>
          {nativeResult&&<p role="status" className="settings-native-result">{nativeResult}</p>}
        </>}
        {tab==="Advanced"&&<><SettingRow icon={Shield} title="Privacy" description="No new telemetry, sync or remote metadata is enabled"><span className="settings-pending">Local-first</span></SettingRow></>}
      </Panel></div>}
    <footer className="settings-footer"><span><Shield size={15}/> Supported changes save locally · advanced options remain in native settings</span>
      <button onClick={onBack} className="settings-mini-button">Back to downloads</button>
      <button className="settings-save" onClick={()=>openNativeDialog("ui.openSettings")} title="Open the existing WPF settings to edit and save"><ExternalLink size={15}/> Edit in native Settings</button>
    </footer>
  </div>;
}
