// SAMPLE — generic usage reference for NotificationKit. No game-specific types.
// Shows the singleton-free flow: the app owns a NotificationHost and reads its Scheduler.
using System;
using Cysharp.Threading.Tasks;
using NotificationKit;
using UnityEngine;

public sealed class NotificationExamples : MonoBehaviour
{
    // Wire this in the inspector (or your own DI) — there is no static Instance.
    [SerializeField] private NotificationHost _host;

    private NotificationScheduler Notifications => _host.Scheduler;

    private async void Start()
    {
        await _host.Initialize();   // registers channels, clears tray, runs executors

        await Notifications.RequestPermissionAsync();

        Notifications.OnNotificationOpened   += HandleNotificationOpened;
        Notifications.OnNotificationReceived += HandleNotificationReceived;
    }

    public void ScheduleDailyReminder()
    {
        var request = NotificationRequest
            .Create("daily_reminder", "Don't forget!", "Your daily check-in is ready.")
            .FireIn(TimeSpan.FromHours(8))
            .WithRepeat(NotificationRepeat.Daily)
            .WithBadge(1);

        var result = Notifications.Schedule(request);
        if (!result.Success)
            Debug.LogError($"Failed to schedule: {result.Error}");
    }

    public void ScheduleEnergyRefill(int minutesUntilFull)
    {
        Notifications.Cancel("energy_refill");
        Notifications.Schedule(NotificationRequest
            .Create("energy_refill", "Energy full!", "You're ready to play again.")
            .FireIn(TimeSpan.FromMinutes(minutesUntilFull))
            .WithData("{\"screen\":\"home\"}")
            .InChannel("game_channel")
            .ShowWhenForegrounded());
    }

    public void ScheduleBatch()
    {
        var requests = new[]
        {
            NotificationRequest.Create("event_mon", "New week!", "Fresh challenges await.").FireIn(TimeSpan.FromDays(1)),
            NotificationRequest.Create("event_fri", "Weekend event", "Special event is live!").FireIn(TimeSpan.FromDays(5)),
        };
        foreach (var r in Notifications.ScheduleAll(requests))
        {
            if (!r.Success)
            {
                Debug.LogWarning($"[NotificationKit] Batch item failed: {r.Error}");
            }
        }
    }

    private void HandleNotificationOpened(ReceivedNotification n)
        => Debug.Log($"User tapped [{n.Id}], data: {n.Data}");   // parse n.Data → deep-link

    private void HandleNotificationReceived(ReceivedNotification n)
        => Debug.Log($"Foreground notification: {n.Title}");

    private void OnDestroy()
    {
        if (_host == null || _host.Scheduler == null)
        {
            return;
        }
        Notifications.OnNotificationOpened   -= HandleNotificationOpened;
        Notifications.OnNotificationReceived -= HandleNotificationReceived;
    }
}
