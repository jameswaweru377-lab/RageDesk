# RangeDesk Scheduler Wake — OPTIONAL Windows integration

The normal scheduler works only while the RangeDesk GUI is running. This
**separate opt-in companion** lets Windows Task Scheduler inspect the local queue
every 5 minutes while you are signed into Windows. When a tracked, incomplete
download is due, it opens RangeDesk and lets the existing application start
the transfer. It does **not** download by itself or run any downloaded files.

## Enabling

1. Extract the full RangeDesk Windows preview ZIP. Keep `RangeDesk.exe`,
   `SchedulerWake/`, and `Scheduler/` together. Do **not** move the files
   after registering.
2. Open PowerShell **as your own user** (administrator rights are not required
   unless your organization's Windows policies demand them).
3. From the extracted RangeDesk folder, run:
   `& .\Scheduler\setup.ps1`
   If your organization blocks PowerShell scripts, don't disable system-wide
   security policies; request approval or continue using the in-app scheduler.
4. To turn the feature off, run: `& .\Scheduler\setup.ps1 -Disable`

Registration creates a **per-user interactive** Windows Scheduled Task,
`RangeDesk - Wake Scheduled Downloads`, with a 5-minute repetition interval.
The helper exits quickly if there are no due items or RangeDesk is already
running. One-time schedules start when due or later; recurring schedules have
one-hour windows and wait for the next window if the computer was asleep.
Only catalog-tracked downloads whose target file is still missing qualify.
It never launches protected Windows services or tries to download while the
user is signed out.

**Security and limitations:** No task is installed by default. This setup
requires explicit user action. The companion reads only local schedules and
local download catalog paths. It never logs signed download URLs, accesses
cookies, or executes downloaded files. The check interval can introduce up to
five minutes of delay. It launches a visible RangeDesk window—not a hidden
download agent. Windows Task Scheduler and corporate group policy may refuse
registration. You must test on your own Windows PC.

**Uninstall note:** Disable the task before uninstalling or moving your
RangeDesk folder. Existing schedules and downloaded files remain intact.

This is a preview; per-user signed installers, operating-system wake-from-sleep
and headless background downloading are not implemented.
