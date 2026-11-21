# App Store — app spec

Distribution: personal

## 1. Vision brief
- **Name:** App Store (personal use; plain name kept by owner's choice)
- **Pitch:** A fast, reliable winget front-end for Sushant's Windows 11 laptop — see every pending update, update them all with one admin popup, and search/install apps — and it always opens.
- **User:** Sushant (single user, keyboard + touchpad, HP Windows 11 laptop).
- **Success:** window visible < 1 s and usable < 1.5 s on cold start even if winget is slow or broken; "Update all" in 2 clicks; never a blank/hung window.

## 2. Features
**v1:** Updates list (winget upgrade) with tick boxes, Update all, per-app Update · Details pane with release notes, publisher, IDs, links · Skip this version (manage in Settings) · Search winget catalog with multi-select "Install selected" · Install a specific version · Operations queue (sequential, progress, cancel, retry) · History log (install/update/skip/fail, filter, export CSV) · Settings: admin once-per-batch toggle (default On), install mode (silent / interactive), check on open, theme, skipped list, diagnostics, About · winget health check + "Fix winget" repair · Local crash log.
**Later:** tray icon + update notifications, start with Windows, scheduled auto-update, Installed/Uninstall page, export/import app list, Scoop/Chocolatey sources.
**Out of scope:** other package managers in v1, accounts, any online service other than winget sources, clipboard features, forced self-update.

## 3. Screen map
NavigationView: Updates (default) · Search · Queue · History · (footer) Settings.
Dialogs: Update/Install confirm (ContentDialog) · Skip version confirm · Fix winget progress · Manage skipped updates · What's new.
Flows: Updates → confirm → Queue → History. Search → tick/Install → confirm → Queue → History. Any page → Settings.

## 4. Screen specs
See mockups/ and design/DESIGN_SPEC.md. Key points:
- **Updates:** header (title, "N updates · checked <time> · total size"), CommandBar: Check again (F5), Update all (N) [accent]. ListView with header tick (tri-state), rows: tick, app tile, name + ID, old → new version, size, "In queue" chip. Right DetailsPane (340 epx, 420 at ≥1600): icon, name, publisher, versions, Update / Skip version, What's new (release notes from manifest), ID/source/installer/license, links. States: checking (skeleton + spinner, buttons disabled, other pages usable), all up to date (empty), winget error (InfoBar + Fix winget), offline (InfoBar: "No internet — showing last check").
- **Search:** AutoSuggestBox-style search (Enter / 300 ms debounce), result count, ListView rows: tick, tile, name, publisher · ID, version, Install or "Installed" chip; selection bar "N selected · Clear · Install selected (N)"; DetailsPane with Install + version picker. States: start (recent searches), no results, error.
- **Queue:** In progress list (progress bar, phase text: Downloading %, Installing…, Waiting), cancel per item, Pause/Cancel all; Finished this session (Done / Failed with Retry + Details). Empty state.
- **History:** grouped by day, filter box, result filter (All/Succeeded/Failed/Skipped), Export CSV.
- **Settings:** SettingsCard groups per mockup 05; About = icon, name, version, "Developed by Sushant Kumar", sushant.sare@gmail.com.
- Shortcuts: Ctrl+F search, F5 check, Ctrl+A select all (in lists), Space toggle tick, Enter open details, Ctrl+Enter Update all / Install selected, Del = Skip (with confirm), Ctrl+, Settings, Esc close dialog/details, Alt access keys.

## 5. Design system
Style 1 "Clean Windows 11" (Fluent 2, Mica, accent #005FB8 light / #60CDFF dark). Icon: "Update loop" (design/icon/app-icon.svg). Full tokens in design/DESIGN_SPEC.md.

## 6. Adaptive window plan
- ≥1008 epx: NavigationView Left (expanded), list + details side by side.
- 641–1007: NavigationView LeftCompact (icons + badge dot), details open as an overlay page with Back.
- <641: LeftMinimal (hamburger), size column and old version hidden, details as full page.
- Max reading width for Settings 1000 epx. Min window 500×500. Works snapped half/quarter, 100–200% scaling, 1366×768 → 4K, mixed-DPI monitors.

## 7. Quality standards
Accessibility: AutomationProperties.Name on every control, full keyboard, visible focus, 4.5:1 contrast, High Contrast themes, text scaling 225%. Localization: all strings in Strings/en-US/Resources.resw. Privacy: no data leaves the PC except winget's own source traffic; data inventory = settings.json, history.db (SQLite), skipped list, crash log. Performance: first frame < 1 s, cold start ≤ 1.5 s, idle CPU ~0%, < 150 MB RAM. Reliability: every winget call has timeout + cancel; COM API with winget.exe CLI fallback; plain-words errors with retry; history kept across updates. Testing: unit tests for parsing/queue/skip logic, UI automation smoke test on the GitHub Windows runner, startup-time check.

## 8. Diagnostics
Global unhandled-exception handler → %LOCALAPPDATA%\Sushant.AppStore\logs\crash-*.log (time, version, Windows build, winget version, stack). Local counters (app starts, updates run, failures). Diagnostics card with "Open log folder". Never logs personal content.

## 9. Technical plan
- App id: Sushant.AppStore · C# 14, .NET 11, WinUI 3 (latest Windows App SDK), unpackaged + Inno Setup per-user Setup.exe, plus portable zip; x64 + ARM64.
- winget access: Microsoft.WindowsPackageManager.ComInterop (Microsoft.Management.Deployment) in-process; fallback to `winget.exe` with `--accept-source-agreements --accept-package-agreements --disable-interactivity` and JSON/table parsing. All calls off the UI thread; window shown before any winget work.
- Elevation: the app itself runs non-admin. When "admin once per batch" is On and a batch contains machine-scope installers, launch one elevated helper (`AppStore.Elevator.exe`, runas) that runs the whole batch and reports progress over a named pipe. Off: plain non-elevated calls; installers raise their own UAC.
- Health check on start: winget present? version? COM activation OK? sources OK? → otherwise error state + Fix winget (Repair-WinGetPackageManager / re-register App Installer, `winget source reset --force` in elevated helper).
- Data: %LOCALAPPDATA%\Sushant.AppStore\ (settings.json, history.db via Microsoft.Data.Sqlite). MVVM with CommunityToolkit.Mvvm, DI via Microsoft.Extensions.Hosting.
- Security level L1. No clipboard use. No self auto-update (manual "check for new version" can come later).

## 10. Fixed rules
Full-fledged; About = name + email only; no clipboard; latest stable stack.
