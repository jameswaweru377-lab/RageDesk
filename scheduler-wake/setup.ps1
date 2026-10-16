# Opt-in per-user, interactive scheduler wake registration for RangeDesk.
# Without this explicit command, no Windows task is registered.
[CmdletBinding()]
param([switch]$Disable)
$ErrorActionPreference = "Stop"
$name = "RangeDesk - Wake Scheduled Downloads"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "SchedulerWake\RangeDesk.SchedulerWake.exe"

if ($Disable) {
  Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
  Write-Host "RangeDesk closed-app wake task disabled for this Windows user."
  exit 0
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
  throw "Scheduler companion missing. Extract the complete RangeDesk portable ZIP."
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
if ([string]::IsNullOrWhiteSpace($identity)) { throw "Cannot identify current Windows user." }
$action = New-ScheduledTaskAction -Execute ((Resolve-Path -LiteralPath $exe).Path)
$trigger = New-ScheduledTaskTrigger -Daily -At "00:00"
$trigger.Repetition.Interval = "PT5M"
$trigger.Repetition.Duration = "P1D"
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings
Register-ScheduledTask -TaskName $name -InputObject $task -Force | Out-Null
Write-Host "RangeDesk closed-app wake is enabled for $identity while signed in."
Write-Host "Every five minutes Windows will check for due scheduled downloads."
Write-Host "It opens RangeDesk only when a tracked, incomplete download is due."
Write-Host "To disable, run this script again with -Disable."
