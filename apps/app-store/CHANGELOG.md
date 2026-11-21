# Changelog

## 1.1.0 — 21 Nov 2026
- Real app icons on Search, Updates and Installed (from the installed program, Store package or the app's website), cached on this PC. New setting: "Download icons for search results".
- Faster search: one PowerShell session with winget's module stays loaded in the background (started with the app), results are remembered for 15 minutes, and version lists use the same session. A note appears if a search takes longer than 4 seconds. Search times are logged in Logs\search.log.
- Bigger app icon on the taskbar, Start menu, title bar and installer.
- Minimise, maximise and close buttons are vertically centred in the taller title bar.
- Sidebar order: Search, Updates, Installed, Queue, History (the app still opens on Updates).

## 1.0.2 — 21 Nov 2026
- Queue, History and Settings really start next to the sidebar now, at every window size and with the sidebar open or closed (1.0.1 didn't fully fix it).
- The page test now also checks that every page starts right beside the sidebar.

## 1.0.1 — 21 Nov 2026
- Queue, History and Settings no longer get cut off on the right when the sidebar is open.
- New "Checking for updates" screen: a progress bar and a card that says which step is running.
- Removed the "You can search for apps while this runs" line.
- Every page is now tested at three window sizes, with the sidebar open and closed, in light and dark.

## 1.0.0 — 21 Nov 2026
First version of App Store, Sushant's own winget app.
- Updates page: every pending winget update, Update all with one admin popup, Skip version, Ignore app, release notes and download sizes.
- Search: find any winget app, tick several and install them together, choose a version.
- Installed: every app on the PC, uninstall one or many, per-app install options.
- Install options per app: Just me / All users, architecture, install folder, stay on a version, skip major updates, close the app first, extra switches.
- Queue with live progress, cancel and retry; History with filter and CSV export.
- Export / import your app list for a new PC.
- Fix winget and Reset sources when Windows Package Manager breaks.
- Collapsible sidebar, light and dark themes, keyboard shortcuts.
