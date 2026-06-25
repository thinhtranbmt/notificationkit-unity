using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NotificationKit
{
    // -------------------------------------------------------------------------
    // NotificationKit.Core — Roxane-free models + the executor seam.
    //
    // Extracted from MyNotification (namespace Core.Notifications). Same packaging
    // convention as IAPKit / HttpKit / DataToolKit: plain folder, no asmdef,
    // isolated by `namespace NotificationKit`, depends only on Unity + UniTask.
    //
    // Fix vs the original: INotificationExecutor used to live in the GLOBAL namespace.
    // It now lives inside the module namespace like every other type.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Game-supplied unit of "decide whether/what to schedule". The host iterates
    /// registered executors on init and on app focus, passing the scheduler in (no global
    /// singleton). This is the SEAM: keep all game-specific logic (entitlements, timers,
    /// data tables) inside your own executor in the app, never in the Kit. See Samples~
    /// for a DailyReward example.
    /// </summary>
    public interface INotificationExecutor
    {
        UniTask Execute(NotificationScheduler scheduler);
    }

    // ─── Enums ────────────────────────────────────────────────────────────────

    public enum NotificationImportance
    {
        Low,
        Default,
        High,
        Critical   // iOS: critical alerts bypass silent mode (requires entitlement)
    }

    public enum NotificationRepeat
    {
        None,
        Hourly,
        Daily,
        Weekly
    }

    public enum NotificationAuthStatus
    {
        NotDetermined,
        Denied,
        Authorized,
        Provisional   // iOS 12+ — delivered quietly to Notification Center
    }

    // ─── Notification Request ─────────────────────────────────────────────────

    [Serializable]
    public sealed class NotificationRequest
    {
        // Required
        public string Id          { get; set; }   // Stable ID used to cancel/update
        public string Title       { get; set; }
        public string Body        { get; set; }

        // Optional content
        public string Subtitle    { get; set; }   // iOS only
        public string SmallIcon   { get; set; }   // Android only (drawable resource name)
        public string LargeIcon   { get; set; }   // Android only
        public string ChannelId   { get; set; } = NotificationChannelConfig.DefaultChannelId;
        public int    BadgeNumber  { get; set; } = -1;  // -1 = do not change badge

        // Scheduling
        public DateTime           FireAt       { get; set; }
        public NotificationRepeat Repeat       { get; set; } = NotificationRepeat.None;

        // Behaviour
        public NotificationImportance Importance { get; set; } = NotificationImportance.Default;
        public bool ShowInForeground             { get; set; } = false;

        /// <summary>
        /// If true, clears the entire notification tray before scheduling this notification.
        /// Useful for preventing "spam" when you only want the most recent notification visible.
        /// </summary>
        public bool ReplaceExisting              { get; set; } = false;

        // Custom payload forwarded to the app when the notification is tapped
        public string Data { get; set; }

        // ── Fluent builder helpers ────────────────────────────────────────────

        public static NotificationRequest Create(string id, string title, string body)
            => new NotificationRequest { Id = id, Title = title, Body = body };

        public NotificationRequest WithSubtitle(string subtitle)         { Subtitle = subtitle;   return this; }
        public NotificationRequest WithBadge(int count)                  { BadgeNumber = count;   return this; }
        public NotificationRequest WithRepeat(NotificationRepeat repeat) { Repeat = repeat;       return this; }
        public NotificationRequest WithData(string json)                 { Data = json;           return this; }
        public NotificationRequest InChannel(string channelId)           { ChannelId = channelId; return this; }
        public NotificationRequest ShowWhenForegrounded()                { ShowInForeground = true; return this; }
        public NotificationRequest ReplacingExisting()                   { ReplaceExisting = true; return this; }
        public NotificationRequest SetSmallIcon(string smallIcon)        { SmallIcon = smallIcon; return this; }
        public NotificationRequest SetLargeIcon(string largeIcon)        { LargeIcon = largeIcon; return this; }

        public NotificationRequest FireIn(TimeSpan delay)
        {
            FireAt = DateTime.Now.Add(delay);
            return this;
        }
    }

    // ─── Delivered notification (received/opened callback payload) ────────────

    public sealed class ReceivedNotification
    {
        public string   Id       { get; internal set; }
        public string   Title    { get; internal set; }
        public string   Body     { get; internal set; }
        public string   Data     { get; internal set; }
        public bool     WasOpened { get; internal set; }  // true = user tapped it
        public DateTime ReceivedAt { get; internal set; }
    }

    // ─── Result ───────────────────────────────────────────────────────────────

    public sealed class NotificationResult
    {
        public bool   Success { get; }
        public string Error   { get; }

        private NotificationResult(bool success, string error = null)
        {
            Success = success;
            Error   = error;
        }

        public static NotificationResult Ok()                 => new NotificationResult(true);
        public static NotificationResult Fail(string message) => new NotificationResult(false, message);

        public override string ToString() => Success ? "OK" : $"FAIL: {Error}";
    }

    // ─── Android channel configuration ────────────────────────────────────────

    [Serializable]
    public sealed class NotificationChannelConfig
    {
        public const string DefaultChannelId   = "default_channel";
        public const string DefaultChannelName = "General";

        public string                 Id          { get; set; }
        public string                 Name        { get; set; }
        public string                 Description { get; set; }
        public NotificationImportance Importance  { get; set; } = NotificationImportance.Default;
        public bool                   EnableLights { get; set; } = true;
        public bool                   EnableVibration { get; set; } = true;

        public static NotificationChannelConfig Default => new NotificationChannelConfig
        {
            Id          = DefaultChannelId,
            Name        = DefaultChannelName,
            Description = "General app notifications",
            Importance  = NotificationImportance.Default,
        };
    }
}
