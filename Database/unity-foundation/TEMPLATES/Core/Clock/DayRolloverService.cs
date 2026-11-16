using System;
using MessagePipe;
using Newtonsoft.Json.Linq;
using R3;
using {{ROOT}}.Core.Common;
using {{ROOT}}.Core.Pause;
using {{ROOT}}.Core.Save;
using {{ROOT}}.Core.Tick;

namespace {{ROOT}}.Core.Clock
{
    public class DayRolloverService : Service, IDayRolloverService, ISaveParticipant
    {
        private const string SAVE_KEY = "dayRollover";
        private const int SAVE_ORDER = 1;

        private readonly ReactiveProperty<int> _currentDay = new(0);

        private readonly IGameClock _clock;
        private readonly ISaveService _saveService;
        private readonly ITickService _tickService;
        private readonly IPauseService _pauseService;
        private readonly IApplicationLifecycle _lifecycle;
        private readonly IPublisher<DayRolloverMessages.DayChanged> _dayChangedPublisher;
        private readonly long _offsetTicks;

        private long _lastDay;
        private bool _isRestored;
        private bool _wasAway = true;

        public DayRolloverService(
            DayRolloverSettings settings,
            IGameClock clock,
            ISaveService saveService,
            ITickService tickService,
            IPauseService pauseService,
            IApplicationLifecycle lifecycle,
            IPublisher<DayRolloverMessages.DayChanged> dayChangedPublisher)
        {
            _clock = clock;
            _saveService = saveService;
            _tickService = tickService;
            _pauseService = pauseService;
            _lifecycle = lifecycle;
            _dayChangedPublisher = dayChangedPublisher;
            _offsetTicks = TimeSpan.FromMinutes(settings.UtcOffsetMinutes).Ticks;
        }

        public string Key => SAVE_KEY;
        public int Order => SAVE_ORDER;

        public ReadOnlyReactiveProperty<int> CurrentDay => _currentDay;

        public DateTime NextRolloverUtc
        {
            get
            {
                long nextDay = GetDay(_clock.UtcNow) + 1L;
                return new DateTime(nextDay * TimeSpan.TicksPerDay - _offsetTicks, DateTimeKind.Utc);
            }
        }

        public TimeSpan TimeUntilNextRollover => NextRolloverUtc - _clock.UtcNow;

        public int GetDay(DateTime utc)
        {
            return (int)((utc.Ticks + _offsetTicks) / TimeSpan.TicksPerDay);
        }

        public object Capture()
        {
            return new DayRolloverSaveData { LastDay = _lastDay };
        }

        public void Restore(JToken section)
        {
            DayRolloverSaveData data = SaveSection.Read<DayRolloverSaveData>(section, SAVE_KEY);
            bool hasSavedDay = data.LastDay > 0;

            _lastDay = hasSavedDay ? data.LastDay : GetDay(_clock.UtcNow);
            _currentDay.Value = (int)_lastDay;
            _isRestored = true;
            _wasAway = true;

            if (hasSavedDay == false)
                _saveService.MarkDirty(SaveReason.Routine);
        }

        protected override void OnInitialize()
        {
            _currentDay.AddTo(Disposables);

            _tickService.SecondTick
                .Subscribe(_ => CheckRollover())
                .AddTo(Disposables);

            _lifecycle.Paused
                .Subscribe(isPaused =>
                {
                    if (isPaused)
                    {
                        _wasAway = true;
                        return;
                    }

                    CheckRollover();
                })
                .AddTo(Disposables);

            _pauseService.IsTimeFrozen
                .Where(isFrozen => isFrozen == false)
                .Subscribe(_ => CheckRollover())
                .AddTo(Disposables);
        }

        private void CheckRollover()
        {
            if (_isRestored == false || _pauseService.IsTimeFrozen.CurrentValue)
                return;

            int today = GetDay(_clock.UtcNow);
            bool wasOffline = _wasAway;

            _wasAway = false;

            if (today <= _lastDay)
                return;

            int daysPassed = (int)(today - _lastDay);
            DateTime dayStartUtc = new(today * TimeSpan.TicksPerDay - _offsetTicks, DateTimeKind.Utc);

            _lastDay = today;
            _currentDay.Value = today;
            _saveService.MarkDirty(SaveReason.Important);

            _dayChangedPublisher.Publish(new DayRolloverMessages.DayChanged(today, daysPassed, wasOffline, dayStartUtc));
        }
    }
}
