$ErrorActionPreference = 'Stop'
$hostExe = Join-Path $PSScriptRoot '..\artifact\RangeDesk\NativeHost\RangeDesk.NativeHost.exe'
if (-not (Test-Path $hostExe)) { throw 'Native host executable not found' }
function Invoke-NativeMessage([string]$Payload) {
  $start = New-Object System.Diagnostics.ProcessStartInfo
  $start.FileName = (Resolve-Path $hostExe).Path
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true
  $start.RedirectStandardOutput = $true
  $start.RedirectStandardError = $true
  $process = [System.Diagnostics.Process]::Start($start)
  try {
    $data = [System.Text.Encoding]::UTF8.GetBytes($Payload)
    $header = [System.BitConverter]::GetBytes([int]$data.Length)
    $process.StandardInput.BaseStream.Write($header,0,4)
    $process.StandardInput.BaseStream.Write($data,0,$data.Length)
    $process.StandardInput.BaseStream.Flush()
    $process.StandardInput.Close()
    $replyHeader = New-Object byte[] 4
    $n = 0
    while ($n -lt 4) {
      $got = $process.StandardOutput.BaseStream.Read($replyHeader,$n,4-$n)
      if ($got -eq 0) { throw 'Native host closed before reply' }
      $n += $got
    }
    $length = [BitConverter]::ToInt32($replyHeader,0)
    if ($length -le 0 -or $length -gt 8192) { throw 'Native reply framing is invalid' }
    $body = New-Object byte[] $length
    $n = 0
    while ($n -lt $length) {
      $got = $process.StandardOutput.BaseStream.Read($body,$n,$length-$n)
      if ($got -eq 0) { throw 'Native reply ended early' }
      $n += $got
    }
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Host process did not exit' }
    if ($process.ExitCode -ne 0) { throw 'Host exited unsuccessfully' }
    return ([Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json)
  }
  finally { $process.Dispose() }
}
$good = Invoke-NativeMessage '{"action":"add_link","url":"https://example.com/manual-download.zip"}'
if (-not $good.accepted) { throw 'Native host rejected valid link' }
$bad = Invoke-NativeMessage '{"action":"read_cookies","url":"https://example.com/"}'
if ($bad.accepted) { throw 'Native host accepted forbidden action' }
$autoWithoutGui = Invoke-NativeMessage '{"action":"capture_download","url":"https://example.com/installer.zip"}'
if ($autoWithoutGui.accepted) { throw 'Native host accepted automatic takeover without running GUI' }
$inbox = Join-Path $env:LOCALAPPDATA 'RangeDesk\browser-inbox'
$captured = Join-Path $inbox '*.json'
$files = @(Get-ChildItem -Path $captured -ErrorAction SilentlyContinue)
if ($files.Count -ne 1) { throw 'Expected exactly one confirmed inbox message' }
Remove-Item -LiteralPath $files[0].FullName -Force
Write-Host 'Native messaging framing, direct-link capture and reject-path checks passed.'
