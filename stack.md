# Proven stack

Latest-stack rule: before every new app (and whenever this file is older than 3 months)
check the newest stable releases, build one app end to end on them, and record the set here.

## Proven set — 21 Nov 2026 (pipeline-check, all checks green)

| Part | Version |
|---|---|
| .NET SDK / C# | 11.0.100 / C# 15 (.NET 10 is the LTS) |
| Windows App SDK / WinUI 3 | 2.7.0 |
| Windows SDK TFM / BuildTools | net11.0-windows10.0.26100.0 / 10.0.26100.4654 |
| Min Windows | 10.0.22000.0 (Windows 11) |
| CommunityToolkit.Mvvm | 8.4.2 |
| Tests | xUnit v3 4.0.3 on Microsoft.Testing.Platform · FlaUI.UIA3 5.0.0 |
| Installer | Inno Setup 6 (per-user, no admin) |
| Publish | Native AOT works for x64 and ARM64 |
| CI | GitHub Actions `windows-latest` (Windows Server 2025, build 26100) |

Measured on the runner: cold start median ~160 ms; x64 publish ~125 MB (self-contained Windows App SDK).

## Lessons (apply to every app)

- **Unpackaged WinUI apps need `<EnableMsixTooling>true</EnableMsixTooling>`**, or the app's own `.pri` (compiled XAML pages) is not produced and every window fails with "XAML parsing failed". The workflow now fails the build if `<App>.pri` is missing.
- **xUnit v3 4.x runs on Microsoft.Testing.Platform:** `global.json` has `"test": { "runner": "Microsoft.Testing.Platform" }`; use `dotnet test --project <csproj> --report-xunit-trx`; no `xunit.runner.visualstudio` / `Microsoft.NET.Test.Sdk`; disable parallel UI tests with `xunit.runner.json`, not the obsolete `CollectionBehavior` attribute.
- **Runner screens are 1024×768:** UI tests maximize the window first, and select/invoke elements through UI Automation patterns (SelectionItem/Invoke), never by mouse position.
- **RadioButtons:** apply settings from each RadioButton's `Checked` event; `RadioButtons.SelectionChanged` does not fire when a radio is checked through UI Automation.
- **Caption buttons** (min/max/close) don't follow an app-set theme by themselves: set `AppWindow.TitleBar.Button*Color` from `Root.ActualTheme` and update on `ActualThemeChanged`.
- **A crash log in `%LOCALAPPDATA%\Sushant\<App>\Logs`** plus collecting it in CI found the first bug in one run — keep it in every app.
- `AppWindow.Resize` takes physical pixels: scale by `GetDpiForWindow / 96`.
