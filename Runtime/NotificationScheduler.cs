using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif

#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

namespace NotificationKit
{
    /// <summary>
    /// Cross-platform LOCAL notification engine (iOS + Android).
    ///
    /// PLAIN class — no MonoBehaviour, no singleton. The notification-center APIs it calls
    /// are static, so the scheduling logic needs no scene object. The app OWNS an instance
    /// (see <see cref="NotificationHost"/> for a drop-in MonoBehaviour that forwards Unity
    /// lifecycle) and holds the reference itself — there is no global Instance.
    ///
    /// Requires: com.unity.mobile.notifications. Roxane-free: all game-specific logic is
    /// supplied through <see cref="INotificationExecutor"/>, never referenced here.
    /// </summary>
    public sealed class NotificationScheduler
    {
        // ─── Events ───────────────────────────────────────────────────────────

        /// <summary>Fires when a notification is received while the app is foregrounded (Android).</summary>
        public event Action<ReceivedNotification> OnNotificationReceived;

        /// <summary>Fires when the user opens the app via a notification tap.</summary>
        public event Action<ReceivedNotification> OnNotificationOpened;

        // ─── State ────────────────────────────────────────────────────────────

        private NotificationAuthStatus _authStatus = NotificationAuthStatus.NotDetermined;
        private readonly List<NotificationChannelConfig> _registeredChannels = new();
        private bool _initialized;

        public NotificationAuthStatus CurrentAuthStatus => _authStatus;
        public bool IsInitialized => _initialized;

        // ─── Initialization ───────────────────────────────────────────────────

        /// <summary>Register Android channels (no-op on iOS) and mark ready. Idempotent.</summary>
        public void Initialize(IEnumerable<NotificationChannelConfig> channels = null)
        {
#if UNITY_ANDROID
            RegisterAndroidChannels(channels);
#endif
            _initialized = true;
        }

        // ─── Permission (UniTask; was StartCoroutine) ──────────────────────────

        /// <summary>
        /// Requests OS notification permission. Android 13+ shows a system dialog; older
        /// Android resolves immediately. iOS shows Apple's dialog on first call.
        /// </summary>
        public async UniTask<NotificationAuthStatus> RequestPermissionAsync()
        {
#if UNITY_IOS && !UNITY_EDITOR
            using var req = new AuthorizationRequest(
                AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound,
                registerForRemoteNotifications: false);

            await UniTask.WaitUntil(() => req.IsFinished);

            _authStatus = req.Granted ? NotificationAuthStatus.Authorized : NotificationAuthStatus.Denied;
            Debug.Log($"[NotificationKit] iOS permission: {_authStatus}");
            return _authStatus;
#elif UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
            {
                _authStatus = NotificationAuthStatus.Authorized;
                return _authStatus;
            }

            var tcs = new UniTaskCompletionSource<NotificationAuthStatus>();
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => tcs.TrySetResult(NotificationAuthStatus.Authorized);
            callbacks.PermissionDenied  += _ => tcs.TrySetResult(NotificationAuthStatus.Denied);
            callbacks.PermissionDeniedAndDontAskAgain += _ => tcs.TrySetResult(NotificationAuthStatus.Denied);

            UnityEngine.Android.Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS", callbacks);

            _authStatus = await tcs.Task;
            return _authStatus;
#else
            await UniTask.CompletedTask;
            Debug.LogWarning("[NotificationKit] Permission request not supported on this platform/editor.");
            _authStatus = NotificationAuthStatus.Denied;
            return _authStatus;
#endif
        }

        // ─── Schedule ─────────────────────────────────────────────────────────

        /// <summary>Schedules a local notification, replacing any with the same ID first.</summary>
        public NotificationResult Schedule(NotificationRequest request)
        {
            if (!_initialized)
                return NotificationResult.Fail("Scheduler not initialized.");

            var validation = Validate(request);
            if (!validation.Success)
                return validation;

            if (request.ReplaceExisting)
                CancelAll();
            else
                Cancel(request.Id); // idempotent — update by re-scheduling

#if UNITY_IOS
            return ScheduleiOS(request);
#elif UNITY_ANDROID
            return ScheduleAndroid(request);
#else
            return NotificationResult.Fail("Unsupported platform.");
#endif
        }

        /// <summary>Schedules multiple notifications in one call.</summary>
        public IReadOnlyList<NotificationResult> ScheduleAll(IEnumerable<NotificationRequest> requests)
        {
            var results = new List<NotificationResult>();
            foreach (var r in requests)
                results.Add(Schedule(r));
            return results;
        }

        // ─── Cancel ───────────────────────────────────────────────────────────

        /// <summary>Cancels a scheduled notification by ID. Safe if not scheduled.</summary>
        public void Cancel(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

#if UNITY_IOS
            iOSNotificationCenter.RemoveScheduledNotification(id);
            iOSNotificationCenter.RemoveDeliveredNotification(id);
#elif UNITY_ANDROID
            var notifId = IdToInt(id);
            AndroidNotificationCenter.CancelScheduledNotification(notifId);
            AndroidNotificationCenter.CancelNotification(notifId);
#endif
        }

        /// <summary>Cancels all pending scheduled notifications and clears the tray.</summary>
        public void CancelAll()
        {
#if UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iOSNotificationCenter.RemoveAllDeliveredNotifications();
#elif UNITY_ANDROID
            AndroidNotificationCenter.CancelAllScheduledNotifications();
            AndroidNotificationCenter.CancelAllNotifications();
#endif
        }

        // ─── Badge ────────────────────────────────────────────────────────────

        /// <summary>Sets the app icon badge number. Pass 0 to clear.</summary>
        public void SetBadge(int count)
        {
#if UNITY_IOS
            iOSNotificationCenter.ApplicationBadge = count;
#elif UNITY_ANDROID
            // Android badge count is driven by notification content; no direct API.
            Debug.Log("[NotificationKit] Android badge count is managed per-notification.");
#endif
        }

        public void ClearBadge() => SetBadge(0);

        // ─── Launch + platform receivers ───────────────────────────────────────

        /// <summary>
        /// Reads the notification that launched the app (if any) and raises
        /// <see cref="OnNotificationOpened"/>. Call once after Initialize. Returns null
        /// when the app was not launched from a notification.
        /// </summary>
        public ReceivedNotification GetLaunchNotification()
        {
#if UNITY_IOS
            return CheckForiOSLaunchNotification();
#elif UNITY_ANDROID
            return CheckForAndroidLaunchNotification();
#else
            return null;
#endif
        }

        /// <summary>
        /// Subscribe to platform delivery callbacks (Android foreground receive). The host
        /// MonoBehaviour calls this in OnEnable; pair with <see cref="UnhookPlatformReceivers"/>.
        /// </summary>
        public void HookPlatformReceivers()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.OnNotificationReceived += OnAndroidNotificationReceived;
#endif
        }

        public void UnhookPlatformReceivers()
        {
#if UNITY_ANDROID
            AndroidNotificationCenter.OnNotificationReceived -= OnAndroidNotificationReceived;
#endif
        }

        // ─── iOS internals ────────────────────────────────────────────────────

#if UNITY_IOS
        private NotificationResult ScheduleiOS(NotificationRequest request)
        {
            try
            {
                var trigger = new iOSNotificationCalendarTrigger
                {
                    Year   = request.FireAt.Year,
                    Month  = request.FireAt.Month,
                    Day    = request.FireAt.Day,
                    Hour   = request.FireAt.Hour,
                    Minute = request.FireAt.Minute,
                    Second = request.FireAt.Second,
                    Repeats = request.Repeat != NotificationRepeat.None
                };

                var notification = new iOSNotification
                {
                    Identifier            = request.Id,
                    Title                 = request.Title,
                    Subtitle              = request.Subtitle ?? string.Empty,
                    Body                  = request.Body,
                    Data                  = request.Data ?? string.Empty,
                    ShowInForeground      = request.ShowInForeground,
                    ForegroundPresentationOption =
                        PresentationOption.Alert | PresentationOption.Sound | PresentationOption.Badge,
                    Trigger = trigger
                };

                if (request.BadgeNumber >= 0)
                    notification.Badge = request.BadgeNumber;

                iOSNotificationCenter.ScheduleNotification(notification);
                return NotificationResult.Ok();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NotificationKit] iOS schedule failed: {ex.Message}");
                return NotificationResult.Fail(ex.Message);
            }
        }

        private ReceivedNotification CheckForiOSLaunchNotification()
        {
            var n = iOSNotificationCenter.GetLastRespondedNotification();
            if (n == null)
            {
                return null;
            }

            var received = new ReceivedNotification
            {
                Id         = n.Identifier,
                Title      = n.Title,
                Body       = n.Body,
                Data       = n.Data,
                WasOpened  = true,
                ReceivedAt = DateTime.Now
            };

            OnNotificationOpened?.Invoke(received);
            return received;
        }
#endif

        // ─── Android internals ────────────────────────────────────────────────

#if UNITY_ANDROID
        private void RegisterAndroidChannels(IEnumerable<NotificationChannelConfig> configs)
        {
            // Always ensure the default channel exists.
            var allChannels = new List<NotificationChannelConfig> { NotificationChannelConfig.Default };
            if (configs != null)
                allChannels.AddRange(configs);

            foreach (var cfg in allChannels)
            {
                if (_registeredChannels.Exists(c => c.Id == cfg.Id)) continue;

                var channel = new AndroidNotificationChannel
                {
                    Id          = cfg.Id,
                    Name        = cfg.Name,
                    Description = cfg.Description,
                    Importance  = ToAndroidImportance(cfg.Importance),
                    EnableLights    = cfg.EnableLights,
                    EnableVibration = cfg.EnableVibration,
                };

                AndroidNotificationCenter.RegisterNotificationChannel(channel);
                _registeredChannels.Add(cfg);

                Debug.Log($"[NotificationKit] Registered Android channel: {cfg.Id}");
            }
        }

        private NotificationResult ScheduleAndroid(NotificationRequest request)
        {
            try
            {
                var notification = new AndroidNotification
                {
                    Title       = request.Title,
                    Text        = request.Body,
                    FireTime    = request.FireAt,
                    IntentData  = request.Data ?? string.Empty,
                    SmallIcon   = string.IsNullOrEmpty(request.SmallIcon) ? "app_icon" : request.SmallIcon,
                };

                if (!string.IsNullOrEmpty(request.LargeIcon))
                    notification.LargeIcon = request.LargeIcon;

                if (request.BadgeNumber >= 0)
                    notification.Number = request.BadgeNumber;

                if (request.Repeat != NotificationRepeat.None)
                    notification.RepeatInterval = ToRepeatInterval(request.Repeat);

                var channelId = string.IsNullOrEmpty(request.ChannelId)
                    ? NotificationChannelConfig.DefaultChannelId
                    : request.ChannelId;

                var notifId = IdToInt(request.Id);
                AndroidNotificationCenter.SendNotificationWithExplicitID(notification, channelId, notifId);
                return NotificationResult.Ok();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NotificationKit] Android schedule failed: {ex.Message}");
                return NotificationResult.Fail(ex.Message);
            }
        }

        private ReceivedNotification CheckForAndroidLaunchNotification()
        {
            var intent = AndroidNotificationCenter.GetLastNotificationIntent();
            if (intent == null)
            {
                return null;
            }

            var received = new ReceivedNotification
            {
                Id         = intent.Id.ToString(),
                Title      = intent.Notification.Title,
                Body       = intent.Notification.Text,
                Data       = intent.Notification.IntentData,
                WasOpened  = true,
                ReceivedAt = DateTime.Now
            };

            OnNotificationOpened?.Invoke(received);
            return received;
        }

        private void OnAndroidNotificationReceived(AndroidNotificationIntentData data)
        {
            var received = new ReceivedNotification
            {
                Id         = data.Id.ToString(),
                Title      = data.Notification.Title,
                Body       = data.Notification.Text,
                Data       = data.Notification.IntentData,
                WasOpened  = false,
                ReceivedAt = DateTime.Now
            };

            OnNotificationReceived?.Invoke(received);
        }

        private static Importance ToAndroidImportance(NotificationImportance importance) => importance switch
        {
            NotificationImportance.Low      => Importance.Low,
            NotificationImportance.High     => Importance.High,
            NotificationImportance.Critical => Importance.High,
            _                               => Importance.Default,
        };

        private static TimeSpan ToRepeatInterval(NotificationRepeat repeat) => repeat switch
        {
            NotificationRepeat.Hourly  => TimeSpan.FromHours(1),
            NotificationRepeat.Daily   => TimeSpan.FromDays(1),
            NotificationRepeat.Weekly  => TimeSpan.FromDays(7),
            _                          => TimeSpan.Zero
        };
#endif

        // ─── Shared helpers ───────────────────────────────────────────────────

        private static NotificationResult Validate(NotificationRequest request)
        {
            if (request == null)
                return NotificationResult.Fail("Request is null.");
            if (string.IsNullOrWhiteSpace(request.Id))
                return NotificationResult.Fail("Notification ID is required.");
            if (string.IsNullOrWhiteSpace(request.Title))
                return NotificationResult.Fail("Notification title is required.");
            if (request.FireAt <= DateTime.Now)
                return NotificationResult.Fail($"FireAt ({request.FireAt}) must be in the future.");
            return NotificationResult.Ok();
        }

        /// <summary>
        /// Stable positive int ID derived from a string ID (Android requires non-negative ints).
        /// </summary>
        private static int IdToInt(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return 0;
            }

            unchecked
            {
                int hash = 23;
                foreach (char c in id)
                    hash = hash * 31 + c;
                return hash & 0x7FFFFFFF;
            }
        }
    }
}
