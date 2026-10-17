@echo off
REM RageDesk is still a WPF-first app during the React migration.
REM Double-click this file to preview the new dark-navy renderer.
REM Keep this launcher beside RangeDesk.exe after extracting the ZIP.
if not exist "%~dp0RangeDesk.exe" (
  echo RangeDesk.exe not found. Extract the full RageDesk preview ZIP first.
  pause
  exit /b 1
)
start "" "%~dp0RangeDesk.exe" --react-preview
