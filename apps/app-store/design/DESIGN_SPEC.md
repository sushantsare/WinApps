# App Store — design spec (approved 21 Nov 2026)

Style 1 "Clean Windows 11" · Fluent 2 · WinUI 3. Mockups in `mockups/` are the visual source of truth.

## Identity
- Name: App Store · Icon: "Update loop" (`icon/app-icon.svg`, `icon/AppIcon.ico` 16–256).
- Font: Segoe UI Variable (system). Icons: Segoe Fluent Icons only.

## Tokens
| Token | Light | Dark |
|---|---|---|
| Accent (fill, links, selection bar) | #005FB8 | #60CDFF |
| Text on accent | #FFFFFF | #000000 |
| Window backdrop | Mica (solid #F3F3F3 fallback) | Mica (#202020 fallback) |
| Page layer | LayerFillColorDefault | LayerFillColorDefault |
| Card | CardBackgroundFillColorDefault | same |
| Stroke | CardStrokeColorDefault | same |
| Success / Warning / Error | SystemFillColorSuccess / Caution / Critical (+ Background variants) | same |
Fixed accent: SystemAccentColor family overridden to the Windows default blue so the app looks the same on every PC. High Contrast: system colours only (no overrides in HighContrast dictionary).

Type ramp (epx): Caption 12 · Body 14 · Body Strong 14 semibold · Subtitle 20 semibold · Title 28 semibold · Title Large 40. Tabular numerals for versions and sizes.
Spacing: 4-epx grid; page padding 32×24 (16 when < 641); card padding 16–20; list row height 56; gaps 8/12/16/24.
Radius: controls 4 · cards/lists/dialogs 8. Motion: WinUI defaults; respects "Animation effects" off.

App tiles (package icons): 36×36 (list), 48×48 (details), radius 8, background = colour at 12% alpha, foreground letter = colour; colour picked from a fixed 12-colour palette by hash of the package ID.

## Shell
- Custom title bar (`TitleBar` control): app icon 16 + "App Store"; caption buttons follow theme.
- NavigationView Left, `IsPaneToggleButtonVisible=True` (☰ collapses to icons / expands to names). First run: open. Remembers last state (Settings › Sidebar: Remember last / Open with names / Icons only). Width < 1008 → LeftCompact, < 641 → LeftMinimal (hamburger in title area).
- Menu: Updates (badge = count) · Search · Installed · Queue (badge = items left) · History · footer Settings.

## Pages (all: title 28 semibold + subtitle caption, command buttons right-aligned in the header)
1. **Updates** — header: "N updates · checked <time> · <total size>", buttons Check again (F5), Update all (N) accent. Left: list card (header row: tri-state tick + "x of y selected" + Sort). Rows: tick · tile · name/ID · old → new (new semibold) · size · "In queue" chip. Under the list: held-back major updates card(s) (warning chip "Major update held back", Install anyway). Right: details panel 340 (420 ≥ 1600): tile 48 + name + publisher, versions + size chip, [Update][Skip version], [Install options…][Ignore app], What's new, divider, ID/Source/Installer/License grid, links. States: checking (skeleton rows + spinner text), up to date (success circle, "All your apps are up to date", Show skipped / Find new apps), winget error (InfoBar error + Try again + Fix winget + explainer card), offline (InfoBar warning).
2. **Search** — search box (Ctrl+F, Enter or 400 ms debounce), result count; list rows: tick · tile · name / publisher · ID · version · [Install] or "Installed" chip; selection bar "N selected · Clear · Install selected (N)"; details panel with Install + version picker. States: start (recent searches chips), no results, error.
3. **Installed** — header: "N apps · M from winget", Source filter (All / winget / Other), Export list. Filter box. Rows: tick · tile · name/ID · tag chip (Install options saved / Skip major updates / Ignored) · version · source. Selection bar "Uninstall selected (N)". Details: Install options… / Uninstall, Saved options grid, ID/Source.
4. **Queue** — "N left · admin approved once for this batch"; Pause queue / Cancel all. In progress list (progress bar under the first item, phase text Downloading x% / Installing… / Uninstalling… / Waiting, cancel ✕). Finished this session (Done chip / Failed text + Retry + Details).
5. **History** — grouped by day, filter box, result filter (All / Succeeded / Failed / Skipped), Export. Row: tile 32 · name/detail · result chip · time.
6. **Settings** — SettingsCard rows in groups: Updates (check on open, show held-back majors, Ignored apps [Manage], Skipped versions [Manage]) · Installing (admin once per batch, install mode, default install for, default architecture, close apps before updating, retry once) · Start & sidebar (open on, sidebar) · Your app list (Export…, Import…) · History (keep for, Clear…) · winget (status line + Reset sources + Fix winget) · Appearance (theme) · About & help (Diagnostics + Open log folder; About card: icon, App Store, version, Developed by Sushant Kumar, sushant.sare@gmail.com, What's new).

## Dialogs (ContentDialog, primary = accent)
- Update/Install/Uninstall confirm: list of names; warning InfoBar for open apps ("<app> is open. It will be closed before updating."); info InfoBar "Windows will ask for admin permission once for the whole batch." when that applies.
- Skip version · Ignore app · Clear history: confirm with plain consequence + where to undo.
- Install options (600 wide): Install for · Architecture · Version · Install folder · Skip major updates · Close the app before updating · Ignore all updates · Extra installer options; Reset to defaults / Save / Cancel.
- Import list: checklist of apps in the file, Install selected.

## Keyboard
Ctrl+F search · F5 check again · Ctrl+A select all · Space tick · Enter details · Ctrl+Enter primary batch action · Del skip/uninstall (with confirm) · Ctrl+, settings · Esc closes dialogs/details · Alt access keys.

## Adaptive
≥ 1008: list + details side by side. 641–1007: compact nav, details open as overlay with Back. < 641: minimal nav, size/old-version columns hidden, details full-page.
