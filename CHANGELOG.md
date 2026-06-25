# Changelog

All notable changes to this package are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/), and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-06-25
### Added
- Initial release.
- `NotificationScheduler` — plain-class local notification engine (iOS + Android): schedule / cancel / badge / UniTask permission / launch-notification, channel registration.
- `NotificationHost` — app-owned MonoBehaviour (no singleton) wiring channels, executors, and Unity lifecycle (focus + platform receivers).
- `INotificationExecutor.Execute(NotificationScheduler)` seam (scheduler injected, no global lookup).
- Optional Firebase push (`FirebaseNotificationManager`) shipped as a separate assembly gated by the `NOTIFICATIONKIT_FIREBASE` define constraint.
- Samples: generic usage + game-specific executor template.
