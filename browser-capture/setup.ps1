# Register the optional RangeDesk native-messaging host for the selected browser.
# Does not require administrator rights; requires manual unpacked extension installation.
[CmdletBinding()]
param(
  [string]$ChromeExtensionId,
  [string]$EdgeExtensionId,
  [switch]$Firefox
)
$ErrorActionPreference = "Stop"
$packageRoot = Split-Path $PSScriptRoot -Parent
$hostExe = Join-Path $packageRoot "NativeHost\RangeDesk.NativeHost.exe"
if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf)) {
  throw "RangeDesk.NativeHost.exe is missing. Use the complete Windows preview ZIP."
}
if (-not ($ChromeExtensionId -or $EdgeExtensionId -or $Firefox)) {
  throw "Specify -ChromeExtensionId, -EdgeExtensionId, or -Firefox. Nothing was registered."
}
$manifestDir = Join-Path $env:LOCALAPPDATA "RangeDesk\native-host-manifests"
New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
function Write-Manifest([string]$Browser, [object]$Extra, [string]$RegistryPath) {
  $manifest = [ordered]@{
    name = "com.rangedesk.capture"
    description = "Opt-in direct-download handoff with GUI acknowledgement for RangeDesk"
    path = (Resolve-Path -LiteralPath $hostExe).Path
    type = "stdio"
  }
  foreach ($key in $Extra.Keys) { $manifest[$key] = $Extra[$key] }
  $filename = Join-Path $manifestDir "com.rangedesk.capture-$Browser.json"
  $json = $manifest | ConvertTo-Json -Depth 6
  [System.IO.File]::WriteAllText($filename,$json,(New-Object System.Text.UTF8Encoding($false)))
  New-Item -Path $RegistryPath -Value $filename -Force | Out-Null
  Write-Host "Registered RangeDesk for $Browser (current Windows user)."
}
function Validate-ExtensionId([string]$Id) {
  if ($Id -cnotmatch '^[a-p]{32}$') {
    throw "Chrome/Edge extension IDs must contain exactly 32 lowercase letters a-p."
  }
}
if ($ChromeExtensionId) {
  Validate-ExtensionId $ChromeExtensionId
  Write-Manifest "chrome" @{ allowed_origins = @("chrome-extension://$ChromeExtensionId/") } "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.rangedesk.capture"
}
if ($EdgeExtensionId) {
  Validate-ExtensionId $EdgeExtensionId
  Write-Manifest "edge" @{ allowed_origins = @("chrome-extension://$EdgeExtensionId/") } "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.rangedesk.capture"
}
if ($Firefox) {
  Write-Manifest "firefox" @{ allowed_extensions = @("rangedesk-capture@local") } "HKCU:\Software\Mozilla\NativeMessagingHosts\com.rangedesk.capture"
}
Write-Host "Restart the selected browser. Right-click links for manual capture."
Write-Host "To opt in to automatic direct-file capture, click the RangeDesk extension toolbar icon (ON badge)."
Write-Host "Keep RangeDesk open; unsupported or unaccepted downloads continue in the browser."
