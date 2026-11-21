# App Store — vision notes

Distribution: personal (Setup.exe for his HP Windows 11 laptop)

## Original vision (his words)
"I want to build a Windows app which will be similar to Unigate UI app." (meant UniGetUI)
"No, I wanted on my own app for this, and sometimes Uniget doesn't open properly."

## Interview
- Purpose: his own replacement for UniGetUI that always opens reliably.
- Sources: "Only Winget as of now."
- Main use: Update all apps; Search & install apps. (Uninstall and backup list not picked.)
- Windows extras (tray, start with Windows, auto-update): no preference → default: normal app, no tray, no startup; checks for updates on every open.
- Admin / UAC: "Let there be a toggle inside the setting for this. Otherwise, every single app update or install would require the UAC pop-up." → Settings toggle: elevate once per batch (default ON) vs let winget ask per app.
- v1 extras picked: Skip/ignore an update; App details before install; Install many together (queue); History log.
- Language: English.
- Name: "App Store" (kept plain, personal use only — warned about Apple trademark / Microsoft Store mix-up, he chose to keep it).
- First page: Updates.
- Input: keyboard + touchpad, no touch/pen.
- Diagnostics: local crash log.

## Suggestions
Picked: ignore update, details pane, multi-install queue, history log.
Not picked (later/never): tray icon, start with Windows, auto-update, uninstall page, backup/restore list, Scoop/Chocolatey/pip.

## Rejected design options
(none yet)

## Design picks
- Style: 1 · Clean Windows 11 (rejected: 2 Bold & colourful, 3 Dark & premium)
- Icon: 2 · Update loop (rejected: 1 Box & arrow, 3 Stacked apps, 4 Download tray, 5 App shelf, 6 Pakka box; an early 'Letter A' idea was dropped before showing — too close to Apple's App Store icon)
- Updates page layout: B · List + details on the right (rejected A simple list, C cards)
- Search page layout: A · List + details on the right with multi-select 'Install selected' bar (rejected B cards, C big search start)

## Round 2 changes (after first mockups)
- Sidebar must be collapsible: ☰ button toggles icons-only ↔ icons+names; starts OPEN on first install; remembers last state.
- Compared with UniGetUI; he agreed to my grouping:
  - v1 ADD: Installed apps page + Uninstall (bulk); Saved install options per app (scope, architecture, location, version, custom switches, reused on updates); Ignore an app forever; Export/import app list (with options); Skip major updates per app (off by default, shows "Major update available"); Close apps before updating (skip if can't close).
  - v2: tray icon + notifications; scheduled checks + auto-update; extra columns (release date, installer domain).
  - Skip: cloud backup (Gist), shortcut control, command line, share links.
