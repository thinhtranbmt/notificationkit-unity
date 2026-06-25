using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NotificationKit
{
    /// <summary>
    /// Drop-in MonoBehaviour the app OWNS — there is NO static Instance / singleton.
    /// Put it on a persistent GameObject and keep the reference yourself (a field, your
    /// own service locator, DI, etc.). It holds a <see cref="NotificationScheduler"/>,
    /// the Android channel list, and the <see cref="INotificationExecutor"/> list, and
    /// forwards Unity lifecycle (focus + platform receivers) to the engine.
    ///
    /// All scheduling lives in the plain <see cref="Scheduler"/>; this class only wires
    /// the bits that genuinely need a scene object.
    /// </summary>
    public sealed class NotificationHost : MonoBehaviour
    {
        [Header("Android Channels")]
        [SerializeField] private List<ChannelDefinition> _channels = new();

        // [SerializeReference] keeps polymorphic executors editable in the inspector with
        // plain Unity (no Textus.SerializeReferenceUI dependency). The app adds its own
        // INotificationExecutor implementations here.
        [SerializeReference] private List<INotificationExecutor> _executors = new();

        /// <summary>The engine. Use it to Schedule/Cancel/SetBadge and subscribe to events.</summary>
        public NotificationScheduler Scheduler { get; private set; }

        private bool _initialized;

        private void Awake()
        {
            Scheduler ??= new NotificationScheduler();
        }

        private void OnEnable()  => Scheduler?.HookPlatformReceivers();
        private void OnDisable() => Scheduler?.UnhookPlatformReceivers();

        /// <summary>
        /// Registers channels, reads any launch notification, clears the tray, and runs the
        /// executors once. Call from your bootstrap flow.
        /// </summary>
        public async UniTask Initialize()
        {
            Scheduler ??= new NotificationScheduler();
            Scheduler.Initialize(BuildChannelConfigs());
            _initialized = true;

            Scheduler.GetLaunchNotification(); // raises OnNotificationOpened if launched via tap
            Scheduler.CancelAll();             // clear tray on startup; executors re-schedule below

            RunExecutors();
            await UniTask.CompletedTask;
        }

        /// <summary>
        /// Runs every registered executor. Each executor re-evaluates the latest game state
        /// and (re)schedules what's still relevant. Fire-and-forget per executor.
        /// </summary>
        public void RunExecutors()
        {
            for (var i = 0; i < _executors.Count; i++)
            {
                try
                {
                    _executors[i]?.Execute(Scheduler).Forget();
                }
                catch (Exception e)
                {
                    // One bad executor must not stop the rest.
                    Debug.LogError($"[NotificationKit] Executor #{i} ({_executors[i]?.GetType().Name}) threw: {e}");
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || !_initialized) return;
            Scheduler.CancelAll();
            RunExecutors();
        }

        private IEnumerable<NotificationChannelConfig> BuildChannelConfigs()
        {
            foreach (var def in _channels)
            {
                yield return new NotificationChannelConfig
                {
                    Id          = def.Id,
                    Name        = def.Name,
                    Description = def.Description,
                    Importance  = def.Importance,
                    EnableLights    = def.EnableLights,
                    EnableVibration = def.EnableVibration,
                };
            }
        }

        // ─── Inspector-serializable channel definition ─────────────────────────

        [Serializable]
        private sealed class ChannelDefinition
        {
            public string                 Id          = "channel_id";
            public string                 Name        = "Channel Name";
            [TextArea(1, 3)]
            public string                 Description = "";
            public NotificationImportance Importance  = NotificationImportance.Default;
            public bool                   EnableLights    = true;
            public bool                   EnableVibration = true;
        }
    }
}
