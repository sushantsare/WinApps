# WinApps

Sushant Kumar's Windows apps, built by Claude.

- Each app lives in `apps/<app>/`.
- `.build-target` names the app the next build should make.
- GitHub Actions (`.github/workflows/build.yml`) builds, tests and packages it on Windows runners and pushes the results (installers, screenshots, test reports) to the `win-builds` branch.
- `stack.md` records the current proven tech stack.

Developer: Sushant Kumar · sushant.sare@gmail.com
