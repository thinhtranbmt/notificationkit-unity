// =============================================================================
// OPTIONAL push (FCM) sub-feature of NotificationKit.
//
// Compiled ONLY when NOTIFICATIONKIT_FIREBASE is defined AND the Firebase Messaging
// SDK (Firebase.Messaging) is present. The base kit (NotificationScheduler /
// NotificationHost) has ZERO Firebase dependency, so a project that doesn't use push
// never needs the SDK. To enable: install Firebase Messaging and add
// NOTIFICATIONKIT_FIREBASE to your Scripting Define Symbols (the package ships this
// file in a separate assembly gated by that define constraint).
// =============================================================================
#if NOTIFICATIONKIT_FIREBASE
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Firebase.Messaging;
using UnityEngine;

namespace NotificationKit
{
    /// <summary>
    /// PUSH (remote) notification manager via Firebase Cloud Messaging.
    ///
    /// PLAIN class, no singleton — the app OWNS it, holds the reference, and calls
    /// <see cref="Dispose"/> on teardown. Roxane-free: it raises C# events for token /
    /// message; the app POSTs the token to its own backend and routes deep-links.
    /// </summary>
    public sealed class FirebaseNotificationManager : IDisposable
    {
        public event Action<string> OnTokenReceived;
        public event Action<FirebaseMessage> OnMessageReceived;
        public event Action<FirebaseMessage> OnNotificationOpened;

        public string FcmToken { get; private set; }
        public bool IsInitialized { get; private set; }

        private const int MaxTokenRetries = 5;
        private const int InitialRetryDelayMs = 1000;

        public async UniTask<bool> InitializeAsync(CancellationToken ct = default)
        {
            if (IsInitialized) return true;

            try
            {
                // CRITICAL for iOS: disable auto token fetch BEFORE subscribing.
                // Prevents the "No APNS token specified" error because the SDK
                // won't try to grab the FCM token until we say so.
                FirebaseMessaging.TokenRegistrationOnInitEnabled = false;

                FirebaseMessaging.TokenReceived   += HandleTokenReceived;
                FirebaseMessaging.MessageReceived += HandleMessageReceived;

                // On iOS this causes registerForRemoteNotifications to be called,
                // which is what gives Firebase the APNs token it needs to bridge.
                // On Android this just requests the POST_NOTIFICATIONS permission (API 33+).
                Debug.Log("[FCM] Requesting notification permission...");
                await FirebaseMessaging.RequestPermissionAsync()
                                       .AsUniTask()
                                       .AttachExternalCancellation(ct);
                Debug.Log("[FCM] Permission flow complete.");

                // Now safe to enable — TokenReceived will fire once APNs is ready (iOS).
                FirebaseMessaging.TokenRegistrationOnInitEnabled = true;

                // Try to fetch immediately, with backoff. On iOS cold launch the APNs
                // token can arrive a beat after permission is granted, so retry.
                FcmToken = await TryGetTokenWithBackoffAsync(ct);

                if (!string.IsNullOrEmpty(FcmToken))
                {
                    LogToken(FcmToken, fromCallback: false);
                }
                else
                {
                    Debug.LogWarning("[FCM] Token not ready yet — will arrive via TokenReceived callback.");
                }

                IsInitialized = true;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FCM] Init failed: {ex.Message}");
                return false;
            }
        }

        private async UniTask<string> TryGetTokenWithBackoffAsync(CancellationToken ct)
        {
            int delay = InitialRetryDelayMs;

            for (int attempt = 1; attempt <= MaxTokenRetries; attempt++)
            {
                try
                {
                    string token = await FirebaseMessaging.GetTokenAsync()
                                                          .AsUniTask()
                                                          .AttachExternalCancellation(ct);
                    if (!string.IsNullOrEmpty(token))
                        return token;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The "No APNS token specified" case lands here on iOS cold start.
                    Debug.LogWarning($"[FCM] GetToken attempt {attempt}/{MaxTokenRetries} failed: {ex.Message}");
                }

                if (attempt < MaxTokenRetries)
                {
                    await UniTask.Delay(delay, cancellationToken: ct);
                    delay = Mathf.Min(delay * 2, 8000); // cap at 8s
                }
            }

            // Not fatal — TokenReceived will still fire later when APNs delivers
            // and HandleTokenReceived will pick it up.
            return null;
        }

        private void HandleTokenReceived(object sender, TokenReceivedEventArgs args)
        {
            FcmToken = args.Token;
            LogToken(args.Token, fromCallback: true);
            OnTokenReceived?.Invoke(args.Token);
            // The app subscribes to OnTokenReceived and POSTs this to its backend.
        }

        private void HandleMessageReceived(object sender, MessageReceivedEventArgs args)
        {
            var msg = args.Message;
            Debug.Log($"[FCM] Message received. From={msg.From} " +
                      $"Title={msg.Notification?.Title} " +
                      $"Body={msg.Notification?.Body} " +
                      $"Opened={msg.NotificationOpened}");

            if (msg.NotificationOpened)
                OnNotificationOpened?.Invoke(msg);   // user tapped notification → deep-link
            else
                OnMessageReceived?.Invoke(msg);      // in-app delivery while foreground
        }

        /// <summary>
        /// Logs the token with a clear platform marker so you can identify which token
        /// to paste into Firebase Console → Test on device.
        /// </summary>
        private void LogToken(string token, bool fromCallback)
        {
            string source = fromCallback ? "Refreshed" : "Initial";

#if UNITY_IOS && !UNITY_EDITOR
            Debug.Log("========================================");
            Debug.Log($"[FCM][iOS][{source}] TOKEN:");
            Debug.Log(token);
            Debug.Log("========================================");
#elif UNITY_ANDROID && !UNITY_EDITOR
            Debug.Log("========================================");
            Debug.Log($"[FCM][Android][{source}] TOKEN:");
            Debug.Log(token);
            Debug.Log("========================================");
#else
            Debug.Log($"[FCM][Editor][{source}] Token: {token}");
#endif
        }

        public UniTask SubscribeAsync(string topic)   => FirebaseMessaging.SubscribeAsync(topic).AsUniTask();
        public UniTask UnsubscribeAsync(string topic) => FirebaseMessaging.UnsubscribeAsync(topic).AsUniTask();

        public void Dispose()
        {
            // Unsubscribe unconditionally (harmless if not subscribed).
            FirebaseMessaging.TokenReceived   -= HandleTokenReceived;
            FirebaseMessaging.MessageReceived -= HandleMessageReceived;
        }
    }
}
#endif
