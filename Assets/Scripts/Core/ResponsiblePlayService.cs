using System;
using System.Globalization;
using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// Responsible play (guardrail 4, plan §5.6): the session timer, the reality checks
    /// and the self-exclusion.
    ///
    /// Three things live here because they are one thing:
    ///
    ///   * **The session timer** counts real play time. Not wall-clock time since launch
    ///     and not time spent with the app in the background — the player's phone
    ///     sitting on a table is not a session, and a timer that counts it would nag a
    ///     player who was never playing.
    ///   * **The reality check** is shown at 15, 30 and 60 minutes. It is shown; it is
    ///     not a lock. Continuing is allowed, because a prompt the player cannot
    ///     decline is not a prompt, it is an obstacle.
    ///   * **The self-exclusion** is the one thing here that is not optional once it is
    ///     chosen. It is one field, one direction, and there is deliberately no way to
    ///     clear it: not in the UI, not in the service, not in a migration.
    ///
    /// The service lives on the services root, so it survives every scene change and
    /// the session keeps counting while the player moves between the lobby and a game.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResponsiblePlayService : MonoBehaviour
    {
        public static ResponsiblePlayService Instance { get; private set; }

        /// <summary>
        /// The sentinel for a permanent exclusion. It is the maximum UTC value, so
        /// "excluded until" needs no special case and no second field: the game is
        /// excluded for as long as the clock can express.
        /// </summary>
        public const string PermanentExclusionUtc = SelfExclusion.PermanentUtc;

        [Header("Reality checks")]
        [Tooltip("Minutes of play at which a reality check is shown.")]
        public int[] realityCheckMinutes = { 15, 30, 60 };

        /// <summary>Raised when a reality check comes due. The minutes elapsed are the payload.</summary>
        public event Action<int> RealityCheckDue;

        /// <summary>Raised when the player excludes themselves.</summary>
        public event Action SelfExcluded;

        private float _sessionSeconds;
        private int _lastCheckMinute;
        private bool _checkPending;
        private long _sessionSpins;
        private long _sessionWagered;

        public float SessionSeconds => _sessionSeconds;
        public int SessionMinutes => Mathf.FloorToInt(_sessionSeconds / 60f);

        /// <summary>Spins taken this session.</summary>
        public long SessionSpins => _sessionSpins;

        /// <summary>Coins wagered this session.</summary>
        public long SessionWagered => _sessionWagered;

        /// <summary>True when a reality check is due and has not been answered yet.</summary>
        public bool RealityCheckPending => _checkPending;

        /// <summary>The minute of the next reality check, or 0 when they have all been shown.</summary>
        public int NextRealityCheckMinute =>
            RealityCheckSchedule.NextDue(realityCheckMinutes, _lastCheckMinute);

        /// <summary>
        /// Is this player currently excluded?
        ///
        /// An expired exclusion is not an exclusion: a one-day exclusion ends after a
        /// day, and the player is expected to be able to come back. A permanent one
        /// never expires.
        /// </summary>
        public bool IsExcluded
        {
            get
            {
                if (!TryGetExclusionEnd(out var until)) return false;
                return DateTime.UtcNow < until;
            }
        }

        /// <summary>True when the exclusion never expires.</summary>
        public bool IsPermanentlyExcluded => SelfExclusion.IsPermanent(ExclusionUntilUtc);

        /// <summary>The raw stored value, for display.</summary>
        public string ExclusionUntilUtc =>
            PlayerProgression.Instance != null ? PlayerProgression.Instance.SelfExcludedUntilUtc : "";

        /// <summary>The moment the exclusion lifts, or null when there is none.</summary>
        public DateTime? ExclusionEndsUtc =>
            TryGetExclusionEnd(out var until) ? until : (DateTime?)null;

        private static DateTime PermanentExclusionEnd
        {
            get
            {
                SelfExclusion.TryGetEnd(PermanentExclusionUtc, out var until);
                return until;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WalletChanged>(OnWalletChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WalletChanged>(OnWalletChanged);
        }

        private void Update()
        {
            // Unscaled, so the clock keeps running while the game is paused — but the
            // player loop stops entirely when the app is backgrounded, so time in the
            // background is not counted as play.
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Advances the session clock.
        ///
        /// Split out from <see cref="Update"/> so the schedule can be driven with a
        /// known number of seconds instead of waiting fifteen minutes of real time for
        /// a test.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return;

            _sessionSeconds += deltaSeconds;
            CheckRealityCheck();
        }

        /// <summary>
        /// The session's own spin and stake counters, taken from the ledger events
        /// rather than from the lifetime stats — a reality check that quotes a
        /// career total is quoting the wrong number.
        /// </summary>
        private void OnWalletChanged(WalletChanged change)
        {
            if (change.currency != Currency.Coins) return;
            if (change.reason != LedgerReason.Spin) return;

            _sessionSpins++;
            if (change.delta < 0) _sessionWagered += -change.delta;
        }

        private void CheckRealityCheck()
        {
            if (_checkPending) return;

            int minutes = Mathf.FloorToInt(_sessionSeconds / 60f);
            if (!RealityCheckSchedule.TryGetDue(realityCheckMinutes, _lastCheckMinute, minutes,
                    out int due))
                return;

            _lastCheckMinute = due;
            _checkPending = true;
            RealityCheckDue?.Invoke(due);
        }

        /// <summary>
        /// The player has seen the reality check. Continuing is allowed — the check is
        /// there to be read, not to be obeyed.
        /// </summary>
        public void AcknowledgeRealityCheck()
        {
            _checkPending = false;
        }

        /// <summary>
        /// Ends the current session.
        ///
        /// The clock and the session counters go back to zero; the exclusion, if there
        /// is one, is untouched. Ending a session is not a punishment.
        /// </summary>
        public void EndSession()
        {
            _sessionSeconds = 0f;
            _sessionSpins = 0;
            _sessionWagered = 0;
            _lastCheckMinute = 0;
            _checkPending = false;
        }

        /// <summary>
        /// Excludes the player for a fixed number of days.
        ///
        /// The window ends on its own: a one-day exclusion that needed a support ticket
        /// to lift would be a permanent one with extra steps.
        /// </summary>
        public bool ExcludeForDays(int days)
        {
            if (days <= 0) return false;
            var until = DateTime.UtcNow.AddDays(days);
            return ApplyExclusion(until.ToString("o", CultureInfo.InvariantCulture), until);
        }

        /// <summary>
        /// Excludes the player permanently.
        ///
        /// There is no undo. That is the point of the feature: a self-exclusion the
        /// player can quietly undo in the next session is not an exclusion.
        /// </summary>
        public bool ExcludePermanently() =>
            ApplyExclusion(PermanentExclusionUtc, PermanentExclusionEnd);

        private bool ApplyExclusion(string stamp, DateTime untilUtc)
        {
            var progression = PlayerProgression.Instance;
            if (progression == null)
            {
                LustLog.Error("[LustHouse] A self-exclusion was requested with no player loaded; " +
                              "it could not be recorded.");
                return false;
            }

            // Never shorten an existing exclusion: the only direction is more.
            if (!SelfExclusion.CanApply(progression.SelfExcludedUntilUtc, stamp))
            {
                LustLog.Info("[LustHouse] The player is already excluded for at least that long.");
                return false;
            }

            progression.SetSelfExclusion(stamp);

            var services = ServicesBootstrap.Instance;
            if (services != null) services.SaveNow();

            LustLog.Info($"[LustHouse] The player excluded themselves until {stamp}.");
            SelfExcluded?.Invoke();
            return true;
        }

        private bool TryGetExclusionEnd(out DateTime untilUtc)
        {
            untilUtc = default;
            var progression = PlayerProgression.Instance;
            if (progression == null) return false;

            string raw = progression.SelfExcludedUntilUtc;
            if (string.IsNullOrEmpty(raw)) return false;

            return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out untilUtc);
        }
    }
}
