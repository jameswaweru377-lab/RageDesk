# RangeDesk Arctic Glass — approved concept 2

The approved design is **Concept 2: Arctic Glass** (light, cool blue,
readable white surfaces). The colorful file and category icons are borrowed
from Concept 3: Cobalt Flow. No other concept palette is used.

## UI contract

* Sidebar: downloads/completed/queue/scheduled, categories and site groups,
  with bright status-colored Windows vector icons. No Labels section visible.
* Downloads: local search, status filters, readable table and compact cards
  when requested. Speed, ETA and real connection counts are not fabricated.
* Confirm / manage: both desktop Add Download and the download details inspector
  share Arctic control styling. The browser-native quality/permission dialog
  uses the same palette and button vocabulary; a confirmed browser download
  moves into the main Downloads tasks **without another desktop confirmation**.
  The browser panel can minimize while the task runs. These are separate
  WPF/Chrome implementations, not literally one UI element.
* Settings: white/light-blue, grouped existing preferences. The original
  validated local preferences, queues and rules remain operable.
* Labels: hidden from sidebar, list, filter, new download and rule forms.
  Legacy saved metadata remains read-compatible to avoid corrupting catalog
  files; it is not destructively migrated.
* No change to engine, safe checkpoint, ETag, strong validator, HTTP 206,
  write-to-temp or SHA-256 checks. Existing running downloads are preserved.

## QA checklist (Windows hardware)

Verify the main table, status filters, sidebar collapse, site presets,
large/small window sizes, scrollable dialogs, focus/keyboard navigation,
Add Download Start/Queue, pause/resume from download details, Settings save,
browser quality picker Start/Download Later, native host and browser collapse
without a duplicate confirmation, app restart with a paused checkpoint.

CI tests prove syntactic/build integration and behavior checks, not
pixel-perfect Windows visual rendering. No dark-mode switch is implied by the
approved Arctic Light design.
