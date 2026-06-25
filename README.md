# NotificationKit

Reusable Unity **local notification** module (iOS + Android), with **optional** Firebase
push as a separately-gated sub-feature. No singleton, no game-specific dependencies — the
engine is a plain class the app owns, mirroring the `HttpKit` / `IAPKit` convention.

## Design

- **`NotificationScheduler`** — PLAIN class (no MonoBehaviour, no singleton). All the
  scheduling logic; the iOS/Android notification-center APIs it calls are static, so it
  needs no scene object. The app owns the instance and holds the reference.
- **`NotificationHost`** — a thin MonoBehaviour the app drops into a persistent scene. It
  owns a `NotificationScheduler`, the Android channel list, and the `INotificationExecutor`
  list (inspector-editable), and forwards Unity lifecycle (focus + platform receivers).
  **There is no static Instance** — access it via your own reference / DI.
- **Executor seam** — `INotificationExecutor.Execute(NotificationScheduler)` is the only
  game-specific hook. The host passes its scheduler in (no global lookup). Keep all
  game logic (entitlements, timers, data tables) in your own executor.

## Files

| File | Role |
|---|---|
| `NotificationKit.Core.cs` | Models, enums, `NotificationRequest` builder, and the `INotificationExecutor` seam. |
| `NotificationScheduler.cs` | The engine — plain class. Schedule / cancel / badge / permission / launch. |
| `NotificationHost.cs` | App-owned MonoBehaviour: channels + executors + lifecycle forwarding. |
| `FirebaseNotificationManager.cs` | **Optional** FCM push. Plain class, guarded by `NOTIFICATIONKIT_FIREBASE`. |

## Requirements

| Dependency | Notes |
|---|---|
| `com.unity.mobile.notifications` | Required (local notifications). |
| UniTask (`com.cysharp.unitask`) | Required. Install separately (Git/OpenUPM). |
| Firebase Messaging | **Only** if you enable push — define `NOTIFICATIONKIT_FIREBASE`. The base kit has zero Firebase dependency. |

## Install

1. **Install UniTask first** (not resolvable from the Unity registry) — add to `Packages/manifest.json`:
   ```json
   "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
   ```
2. **Install NotificationKit** (Mobile Notifications auto-resolves from the registry):
   ```json
   "com.mycore.notificationkit": "https://github.com/thinhtranbmt/notificationkit-unity.git#v0.1.0"
   ```
   Or via Package Manager UI → *Add package from git URL…*. Drop `#v0.1.0` to track `main`.

> Push ships as a separate assembly (`MyCore.NotificationKit.Firebase`) gated by the
> `NOTIFICATIONKIT_FIREBASE` define constraint — excluded from compilation entirely unless
> you define that symbol, so projects without Firebase build cleanly.

## Usage (singleton-free)

```csharp
// On a persistent GameObject: add a NotificationHost component, fill its channels +
// executors in the inspector, and keep a reference to it (field / your service locator).
await host.Initialize();                      // registers channels, clears tray, runs executors

NotificationScheduler n = host.Scheduler;
await n.RequestPermissionAsync();

n.Schedule(NotificationRequest
    .Create("daily", "Don't forget!", "Your reward is ready.")
    .FireIn(TimeSpan.FromHours(8)));

n.OnNotificationOpened += received => { /* deep-link from received.Data */ };
```

### Custom executor
```csharp
[Serializable]
public class MyExecutor : INotificationExecutor
{
    public async UniTask Execute(NotificationScheduler scheduler)
    {
        scheduler.Schedule(NotificationRequest.Create("id", "Title", "Body").FireIn(...));
        await UniTask.CompletedTask;
    }
}
```

### Optional push
Install Firebase Messaging and add `NOTIFICATIONKIT_FIREBASE` to Scripting Define Symbols.
Then the app owns a `FirebaseNotificationManager`, calls `InitializeAsync()`, subscribes to
`OnTokenReceived` (POST the token to your backend), and calls `Dispose()` on teardown.

## Samples
`Samples~/NotificationExamples.cs` — generic usage. `Samples~/RoxaneNotificationAdapters.cs`
— a game-specific executor example (guarded by `NOTIFICATIONKIT_SAMPLES`).
