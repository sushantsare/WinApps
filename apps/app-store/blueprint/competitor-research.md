# Competitor research — winget front-ends (Nov 2026)

| App | Notes |
|---|---|
| UniGetUI (Devolutions, MIT) | Multi-manager GUI (WinGet, Scoop, Choco, pip, npm, .NET, PS Gallery, Cargo). Pages: Discover, Updates, Installed, Bundles, Settings. 2026.2.3 switched to NativeAOT; some builds refused to start (WinGet/COM init issues) — hotfixed in 2026.2.4. Startup cost grows with each manager it loads. Users complain about forced auto-update on close. |
| Winstall.app | Web only; browse winget catalog, build shareable collections, generates install commands. No local updates. |
| Ninite | ~100 curated apps, one-click bulk installer, no catalog search. |
| Microsoft Store | Store apps only; updates page with "Get updates". Familiar Windows 11 library layout. |
| Patch My PC / UCheck | Updater-first, list with checkboxes, update selected. |

## Lessons for "App Store"
- Window must appear instantly; all winget work async after first frame.
- Use Microsoft.Management.Deployment (WinGet COM API) with winget.exe CLI fallback; detect broken/missing winget and offer a repair action (App Installer / Repair-WinGetPackageManager).
- Updates page: checkbox list + "Update all" primary action + per-row Update; skip/ignore version.
- Search page: results list with Install, multi-select queue, details pane (publisher, version, homepage, license, release notes).
- Operations queue visible (progress per item), history log.
- No forced self-updates.
