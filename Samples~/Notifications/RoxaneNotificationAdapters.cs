// -----------------------------------------------------------------------------
// SAMPLE (NOT COMPILED) — Unity ignores any folder ending in `~`.
//
// This is the ONE game-specific piece that must NOT live inside the Kit: an
// INotificationExecutor implementation that reaches into Roxane (ServiceLocator,
// GameData, DailyRewardManager). It is the original MyNotification/InGame/
// DailyRewardNotificationExecutor.cs, retargeted onto NotificationKit.
//
// Copy this into your app folder (drop the `~`) and add an instance to the
// NotificationHost executors list in the inspector. The host passes the scheduler in.
//
// References app-specific types (ServiceLocator, GameData, DailyRewardManager) that
// won't exist in a fresh project, so it is guarded by NOTIFICATIONKIT_SAMPLES and stays
// inert by default. Read it as a reference; to compile, add NOTIFICATIONKIT_SAMPLES to
// your Scripting Define Symbols and adapt the type names.
// -----------------------------------------------------------------------------
#if NOTIFICATIONKIT_SAMPLES
using System;
using Cysharp.Threading.Tasks;
using Game4Creators;
using GameData;
using NotificationKit;
using UnityEngine;

namespace MyCore.Notifications.InGame
{
    [Serializable]
    public class DailyRewardNotificationExecutor : INotificationExecutor
    {
        private ClientMiscCollection ClientMiscCollection => ServiceLocator.Instance.GetService<ClientMiscCollection>();

        public async UniTask Execute(NotificationScheduler scheduler)
        {
            if (DailyRewardManager.Instance == null)
                return;

            if (DailyRewardManager.Instance.HasRewardCanClaim())
                return;

            long duration = DailyRewardManager.Instance.GetRemainTimeCanClaimReward();
            if (duration <= 0)
                return;

            if (!ClientMiscCollection.TryGetNotificationData(NotificationPlace.DailyReward, out NotificationDataEntry dataEntry))
                return;

            var request = NotificationRequest
                .Create(dataEntry.id, dataEntry.headerText, dataEntry.descriptionText)
                .FireIn(TimeSpan.FromSeconds(duration))
                .SetSmallIcon("icon_small")
                .SetLargeIcon("icon_large");

            scheduler.Schedule(request);
            Debug.Log("[Notification] Scheduled Daily Reward notification");

            await UniTask.CompletedTask;
        }
    }
}
#endif
