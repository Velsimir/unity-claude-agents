using System;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using R3;
using {{ROOT}}.Core.Common;
using {{ROOT}}.Core.Save;

namespace {{ROOT}}.Core.Clock
{
    public class GameClock : Service, IGameClock, ISaveParticipant
    {
        private const string SAVE_KEY = "clock";
        private const int SAVE_ORDER = 0;

        private static readonly TimeSpan DriftTolerance = TimeSpan.FromMinutes(2);

        private readonly Stopwatch _sessionStopwatch = new();
        private readonly IApplicationLifecycle _lifecycle;

        private DateTime _sessionBaseUtc;
        private long _maxSeenUtcTicks;
        private bool _isTrusted = true;

        public GameClock(IApplicationLifecycle lifecycle)
        {
            _lifecycle = lifecycle;
            Rebase();
        }

        public string Key => SAVE_KEY;
        public int Order => SAVE_ORDER;

        public bool IsTrusted => _isTrusted;

        public DateTime UtcNow
        {
            get
            {
                long ticks = ResolveTicks();

                if (ticks > _maxSeenUtcTicks)
                    _maxSeenUtcTicks = ticks;

                return new DateTime(_maxSeenUtcTicks, DateTimeKind.Utc);
            }
        }

        public TimeSpan GetElapsedSince(long utcTicks, TimeSpan maxElapsed)
        {
            if (utcTicks <= 0)
                return TimeSpan.Zero;

            TimeSpan elapsed = UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);

            if (elapsed <= TimeSpan.Zero)
                return TimeSpan.Zero;

            return elapsed > maxElapsed ? maxElapsed : elapsed;
        }

        public object Capture()
        {
            return new GameClockSaveData { MaxSeenUtcTicks = _maxSeenUtcTicks };
        }

        public void Restore(JToken section)
        {
            GameClockSaveData data = SaveSection.Read<GameClockSaveData>(section, SAVE_KEY);

            if (data.MaxSeenUtcTicks > _maxSeenUtcTicks)
                _maxSeenUtcTicks = data.MaxSeenUtcTicks;
        }

        protected override void OnInitialize()
        {
            _lifecycle.Paused
                .Where(isPaused => isPaused == false)
                .Subscribe(_ => Rebase())
                .AddTo(Disposables);
        }

        private void Rebase()
        {
            _sessionBaseUtc = DateTime.UtcNow;
            _sessionStopwatch.Restart();
        }

        private long ResolveTicks()
        {
            DateTime expected = _sessionBaseUtc + _sessionStopwatch.Elapsed;
            DateTime system = DateTime.UtcNow;

            TimeSpan drift = system - expected;

            if (drift < TimeSpan.Zero)
                drift = drift.Negate();

            if (drift <= DriftTolerance)
                return system.Ticks;

            if (_isTrusted)
            {
                _isTrusted = false;
                GameDebug.LogWarning($"[Clock] Системное время разошлось с монотонным на {drift}, " +
                    "дальше в этой сессии используется монотонное.");
            }

            return expected.Ticks;
        }
    }
}
