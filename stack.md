# Proven stack

Latest-stack rule: before every new app (and whenever this file is older than 3 months)
check the newest stable releases, build one app end to end on them, and record the set here.

## Target set (21 Nov 2026)

| Part | Version | Notes |
|---|---|---|
| .NET SDK / C# | .NET 11 (11.0.1xx) / C# 15 | GA 10 Nov 2026 (STS); .NET 10 is the LTS |
| Windows App SDK / WinUI 3 | newest stable 2.x | 2.4.0 was current Aug 2026; resolved per build |
| Windows SDK TFM | 10.0.26100.0 | move to 10.0.28000.0 once a build proves it |
| Min Windows | 10.0.22000.0 (Windows 11) | |
| Design | Fluent 2 (Mica, TitleBar, NavigationView) | |
| Tests | xUnit v3, FlaUI UIA3 | Axe.Windows added with the first real app |
| Installer | Inno Setup 6 (per-user, no admin) | |
| CI | GitHub Actions `windows-latest` | results on branch `win-builds` |

## Resolved package versions

_Filled in from the first green `pipeline-check` build (out/audit/packages.txt)._

## Lessons

_Rules learned from audits that apply to every app go here._
