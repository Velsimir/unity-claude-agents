using System;
using System.Collections.Generic;
using MessagePipe;
using Newtonsoft.Json.Linq;
using R3;
using Zenject;
using {{ROOT}}.Core.Clock;
using {{ROOT}}.Core.Common;
using {{ROOT}}.Core.Pause;
using {{ROOT}}.Core.Save;
using {{ROOT}}.Core.Tick;

namespace {{ROOT}}.Core.Timers
{
    public class TimerService : Service, ITimerService, ISaveParticipant
    {
        private const string SAVE_KEY = "timers";
        private const int SAVE_ORDER = 5;

        private readonly Dictionary<string, TimerEntry> _timers = new();
        private readonly List<TimerEntry> _expired = new();

        private readonly IGameClock _clock;
        private readonly ISaveService _saveService;
        private readonly ITickService _tickService;
        private readonly IPauseService _pauseService;
        private readonly IApplicationLifecycle _lifecycle;
        private readonly IPublisher<TimerMessages.Completed> _completedPublisher;
        private readonly LazyInject<List<ITimerOwner>> _ownersSource;

        private List<ITimerOwner> _owners;
        private bool _isDispatching;

        public TimerService(
            IGameClock clock,
            ISaveService saveService,
            ITickService tickService,
            IPauseService pauseService,
            IApplicationLifecycle lifecycle,
            IPublisher<TimerMessages.Completed> completedPublisher,
            [InjectOptional] LazyInject<List<ITimerOwner>> owners)
        {
            _clock = clock;
            _saveService = saveService;
            _tickService = tickService;
            _pauseService = pauseService;
            _lifecycle = lifecycle;
            _completedPublisher = completedPublisher;
            _ownersSource = owners;
        }

        public string Key => SAVE_KEY;
        public int Order => SAVE_ORDER;

        private IReadOnlyList<ITimerOwner> Owners
        {
            get
            {
                if (_owners != null)
                    return _owners;

                _owners = new List<ITimerOwner>();

                List<ITimerOwner> source = _ownersSource?.Value;

                if (source == null)
                    return _owners;

                foreach (ITimerOwner owner in source)
                {
                    if (string.IsNullOrEmpty(owner.TimerPrefix))
                    {
                        GameDebug.LogConfigurationError(
                            $"[Timers] У владельца {owner.GetType().Name} пустой TimerPrefix.");
                        continue;
                    }

                    _owners.Add(owner);
                }

                return _owners;
            }
        }

        public bool IsRunning(string id)
        {
            return string.IsNullOrEmpty(id) == false && _timers.ContainsKey(id);
        }

        public TimeSpan GetRemaining(string id)
        {
            if (TryGetEndUtc(id, out DateTime endUtc) == false)
                return TimeSpan.Zero;

            TimeSpan remaining = endUtc - _clock.UtcNow;

            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        public bool TryGetEndUtc(string id, out DateTime endUtc)
        {
            if (IsRunning(id) == false)
            {
                endUtc = default;
                return false;
            }

            endUtc = new DateTime(_timers[id].EndUtcTicks, DateTimeKind.Utc);
            return true;
        }

        public bool TryStart(string id, TimeSpan duration)
        {
            if (IsValidId(id) == false || _timers.ContainsKey(id))
                return false;

            _timers[id] = CreateEntry(id, duration);
            _saveService.MarkDirty(SaveReason.Important);

            return true;
        }

        public bool Reschedule(string id, TimeSpan duration)
        {
            if (IsValidId(id) == false)
                return false;

            bool isCreated = _timers.ContainsKey(id) == false;

            _timers[id] = CreateEntry(id, duration);
            _saveService.MarkDirty(SaveReason.Important);

            return isCreated;
        }

        public bool Cancel(string id)
        {
            if (IsRunning(id) == false)
                return false;

            _timers.Remove(id);
            _saveService.MarkDirty(SaveReason.Important);

            return true;
        }

        public bool Skip(string id)
        {
            if (IsRunning(id) == false)
                return false;

            _timers[id].EndUtcTicks = _clock.UtcNow.Ticks;
            _saveService.MarkDirty(SaveReason.Important);
            CompleteExpired();

            return true;
        }

        public object Capture()
        {
            TimerSaveData data = new();

            foreach (TimerEntry entry in _timers.Values)
                data.Timers.Add(new TimerSaveEntry { Id = entry.Id, EndUtcTicks = entry.EndUtcTicks });

            return data;
        }

        public void Restore(JToken section)
        {
            TimerSaveData data = SaveSection.Read<TimerSaveData>(section, SAVE_KEY);

            _timers.Clear();

            if (data.Timers == null)
                return;

            foreach (TimerSaveEntry saved in data.Timers)
            {
                if (IsValidId(saved.Id) == false)
                    continue;

                _timers[saved.Id] = new TimerEntry(saved.Id, saved.EndUtcTicks, isRestored: true);
            }
        }

        protected override void OnInitialize()
        {
            _tickService.SecondTick
                .Subscribe(_ => CompleteExpired())
                .AddTo(Disposables);

            _pauseService.IsTimeFrozen
                .Where(isFrozen => isFrozen == false)
                .Subscribe(_ => CompleteExpired())
                .AddTo(Disposables);

            _lifecycle.Paused
                .Where(isPaused => isPaused == false)
                .Subscribe(_ => CompleteExpired())
                .AddTo(Disposables);
        }

        private TimerEntry CreateEntry(string id, TimeSpan duration)
        {
            TimeSpan safeDuration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero;

            return new TimerEntry(id, (_clock.UtcNow + safeDuration).Ticks, isRestored: false);
        }

        private bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) == false)
                return true;

            GameDebug.LogConfigurationError("[Timers] Пустой идентификатор таймера.");
            return false;
        }

        private void CompleteExpired()
        {
            if (_isDispatching || _timers.Count == 0 || _pauseService.IsTimeFrozen.CurrentValue)
                return;

            long nowTicks = _clock.UtcNow.Ticks;

            _expired.Clear();

            foreach (TimerEntry entry in _timers.Values)
            {
                if (entry.EndUtcTicks <= nowTicks)
                    _expired.Add(entry);
            }

            if (_expired.Count == 0)
                return;

            _expired.Sort((left, right) => left.EndUtcTicks.CompareTo(right.EndUtcTicks));

            foreach (TimerEntry entry in _expired)
                _timers.Remove(entry.Id);

            _saveService.MarkDirty(SaveReason.Important);

            _isDispatching = true;

            try
            {
                foreach (TimerEntry entry in _expired)
                    Dispatch(entry, nowTicks);
            }
            finally
            {
                _isDispatching = false;
                _expired.Clear();
            }
        }

        private void Dispatch(TimerEntry entry, long nowTicks)
        {
            TimerMessages.Completed completion = new(
                entry.Id,
                new DateTime(entry.EndUtcTicks, DateTimeKind.Utc),
                TimeSpan.FromTicks(nowTicks - entry.EndUtcTicks),
                entry.IsRestored);

            foreach (ITimerOwner owner in Owners)
            {
                if (entry.Id.StartsWith(owner.TimerPrefix, StringComparison.Ordinal) == false)
                    continue;

                try
                {
                    owner.OnTimerCompleted(completion);
                }
                catch (Exception exception)
                {
                    GameDebug.LogError($"[Timers] Владелец «{owner.TimerPrefix}» упал на таймере «{entry.Id}»: " +
                        $"{exception.Message}");
                }
            }

            _completedPublisher.Publish(completion);
        }

        private sealed class TimerEntry
        {
            public TimerEntry(string id, long endUtcTicks, bool isRestored)
            {
                Id = id;
                EndUtcTicks = endUtcTicks;
                IsRestored = isRestored;
            }

            public string Id { get; }
            public long EndUtcTicks { get; set; }
            public bool IsRestored { get; }
        }
    }
}
