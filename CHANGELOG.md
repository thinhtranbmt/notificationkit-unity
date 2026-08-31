# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-08-31
### Fixed
- Declare `com.cysharp.unitask` in `package.json`. The runtime assembly referenced the
  `UniTask` assembly while the manifest declared nothing, so a fresh git-URL install left
  the reference unresolved. UniTask resolves from the OpenUPM scoped registry — see README.
- Commit Unity `.meta` files for every asset and folder. Without them Unity treats the
  package as an immutable folder with unimported assets and silently ignores all sources,
  so nothing compiled when the package was installed via UPM git URL.

### Changed
- Sample adapter file renamed off the old game's name; runtime doc comments too.

## [0.1.0] - 2026-06-25
### Added
- Initial release.
- `NotificationScheduler` — plain-class local notification engine (iOS + Android): schedule / cancel / badge / UniTask permission / launch-notification, channel registration.
- `NotificationHost` — app-owned MonoBehaviour (no singleton) wiring channels, executors, and Unity lifecycle (focus + platform receivers).
- `INotificationExecutor.Execute(NotificationScheduler)` seam (scheduler injected, no global lookup).
- Optional Firebase push (`FirebaseNotificationManager`) shipped as a separate assembly gated by the `NOTIFICATIONKIT_FIREBASE` define constraint.
- Samples: generic usage + game-specific executor template.
