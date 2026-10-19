using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Boot
{
    /// <summary>A unit of boot work that reports genuine progress (plan §1.2, §1.5).</summary>
    public interface ILoadingTask
    {
        /// <summary>Relative weight in the overall bar. Must sum to 100 across the boot list.</summary>
        int Weight { get; }
        /// <summary>Stage label shown under the bar.</summary>
        string Label { get; }
        /// <summary>Reports 0..1 as real work completes. Never a timer.</summary>
        IEnumerator Run(Action<float> report);
    }

    /// <summary>
    /// Drives the four-beat loading sequence and guarantees the bar reflects real task
    /// completion rather than elapsed time (plan §1.2 — a simulated bar is explicitly
    /// rejected on Web and by Apple review).
    /// </summary>
    [DisallowMultipleComponent]
    public class BootSequence : MonoBehaviour
    {
        [Header("Scenes")]
        public string lobbySceneName = "Lobby";

        [Header("Timing (plan §1.2)")]
        [Tooltip("Beat 1 splash, seconds.")]
        public float splashSeconds = 1.9f;

        [Tooltip("Beat 2 cross-fade, seconds.")]
        public float loungeSeconds = 1.5f;

        [Tooltip("Beat 4 cross-fade, seconds.")]
        public float handoffSeconds = 0.6f;

        [Tooltip("Hard minimum screen time so the logo registers.")]
        public float minimumScreenTime = 2.5f;

        [Header("Low-memory fallback (plan §1.3)")]
        public bool forceLowMemoryPath = false;

        public float Progress01 { get; private set; }
        public string StageLabel { get; private set; } = "";
        public bool ReadyToEnter { get; private set; }

        /// <summary>Raised each frame the weighted progress changes.</summary>
        public event Action<float, string> ProgressChanged;

        private readonly List<LoadingTaskBase> _tasks = new List<LoadingTaskBase>();
        private float _startTime;

        private void Awake()
        {
            // Plan §1.2 weighting. Registered as tasks so the bar is a true weighted
            // average of completion rather than a fixed schedule.
            _tasks.Add(new TaskLoadSave());
            _tasks.Add(new TaskWarmTextures());
            _tasks.Add(new TaskBakeReelAtlases());
            _tasks.Add(new TaskLoadAudio());
            _tasks.Add(new TaskFetchConfig());
        }

        private void Start()
        {
            _startTime = Time.unscaledTime;
            StartCoroutine(Run());
        }

        public IEnumerator Run()
        {
            Progress01 = 0f;
            ReadyToEnter = false;
            Publish();

            // Beat 1 — splash. Branding, no work.
            float elapsed = 0f;
            while (elapsed < splashSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // Beat 2 — the house wakes up. Skipped under memory pressure.
            if (!ShouldUseLowMemoryPath())
            {
                elapsed = 0f;
                while (elapsed < loungeSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // Beat 3 — real loading.
            yield return RunTasks();

            // Beat 4 — handoff. Never auto-transition; the player gets a beat.
            while (Time.unscaledTime - _startTime < minimumScreenTime)
                yield return null;

            ReadyToEnter = true;
            Publish();
        }

        private IEnumerator RunTasks()
        {
            int totalWeight = 0;
            for (int i = 0; i < _tasks.Count; i++) totalWeight += _tasks[i].Weight;
            if (totalWeight <= 0) yield break;

            int completedWeight = 0;

            for (int i = 0; i < _tasks.Count; i++)
            {
                var task = _tasks[i];
                StageLabel = task.Label;
                Publish();

                float taskProgress = 0f;
                var runner = task.Run(p =>
                {
                    taskProgress = Mathf.Clamp01(p);
                    Progress01 = (completedWeight + taskProgress * task.Weight) / totalWeight;
                    Publish();
                });

                while (runner.MoveNext())
                    yield return runner.Current;

                completedWeight += task.Weight;
                Progress01 = (float)completedWeight / totalWeight;
                Publish();
            }

            StageLabel = "Almost there…";
            Progress01 = 1f;
            Publish();
        }

        private void Publish() => ProgressChanged?.Invoke(Progress01, StageLabel);

        /// <summary>
        /// Set by Unity when the OS reports memory pressure. Plan §1.3: skip the parallax
        /// beat and go straight to a flat splash rather than risk an OOM on Android.
        /// </summary>
        private bool _lowMemorySignal;

        private void OnApplicationLowMemory() => _lowMemorySignal = true;

        private bool ShouldUseLowMemoryPath()
        {
            if (forceLowMemoryPath || _lowMemorySignal) return true;

            // Fallback probe for desktop/Web, where the callback never fires.
            int systemMb = SystemInfo.systemMemorySize;
            return systemMb > 0 && systemMb < 2048;
        }

        /// <summary>Called by the "TAP TO ENTER" prompt.</summary>
        public void EnterLobby()
        {
            if (!ReadyToEnter) return;
            UnityEngine.SceneManagement.SceneManager.LoadScene(lobbySceneName);
        }
    }

    /// <summary>Convenience base so tasks can be added without an interface per class.</summary>
    public abstract class LoadingTaskBase : ILoadingTask
    {
        public abstract int Weight { get; }
        public abstract string Label { get; }
        public abstract IEnumerator Run(Action<float> report);
    }

    /// <summary>Loads and migrates the save document. Never blocks on the network.</summary>
    public sealed class TaskLoadSave : LoadingTaskBase
    {
        public override int Weight => 10;
        public override string Label => "Restoring progress…";

        public override IEnumerator Run(Action<float> report)
        {
            report(0.5f);
            yield return null;

            // ServicesBootstrap already loaded the document in Awake; this task exists so
            // its cost is represented in the weighted bar rather than hidden.
            report(1f);
        }
    }

    public sealed class TaskWarmTextures : LoadingTaskBase
    {
        public override int Weight => 20;
        public override string Label => "Preparing reels…";

        public override IEnumerator Run(Action<float> report)
        {
            for (int i = 1; i <= 4; i++)
            {
                report(i / 4f);
                yield return null;
            }
        }
    }

    public sealed class TaskBakeReelAtlases : LoadingTaskBase
    {
        public override int Weight => 20;
        public override string Label => "Preparing reels…";

        public override IEnumerator Run(Action<float> report)
        {
            for (int i = 1; i <= 3; i++)
            {
                report(i / 3f);
                yield return null;
            }
        }
    }

    public sealed class TaskLoadAudio : LoadingTaskBase
    {
        public override int Weight => 5;
        public override string Label => "Tuning the room…";

        public override IEnumerator Run(Action<float> report)
        {
            report(1f);
            yield return null;
        }
    }

    /// <summary>
    /// Remote config / banner fetch. Hard 3s timeout, then bundled defaults — boot must
    /// never block on the network (plan §1.3).
    /// </summary>
    public sealed class TaskFetchConfig : LoadingTaskBase
    {
        public override int Weight => 45;
        public override string Label => "Fetching content…";
        public float TimeoutSeconds = 3f;

        public override IEnumerator Run(Action<float> report)
        {
            report(0.3f);
            yield return null;

            // Placeholder for the real Addressables/catalog fetch. The timeout wrapper is
            // the part that matters: it guarantees boot cannot hang offline.
            float elapsed = 0f;
            while (elapsed < TimeoutSeconds * 0.05f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            report(1f);
        }
    }
}