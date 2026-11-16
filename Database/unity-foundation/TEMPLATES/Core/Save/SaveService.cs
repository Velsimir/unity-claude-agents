using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using R3;
using UnityEngine;
using Zenject;
using {{ROOT}}.Core.Clock;
using {{ROOT}}.Core.Common;
using {{ROOT}}.Core.Pause;
using {{ROOT}}.Core.Tick;

namespace {{ROOT}}.Core.Save
{
    public class SaveService : Service, ISaveService
    {
        private const float AUTOSAVE_INTERVAL_SECONDS = 30f;

        private readonly LazyInject<List<ISaveParticipant>> _participantsSource;
        private readonly LazyInject<List<ISaveMigration>> _migrationsSource;
        private readonly ISaveStorage _localStorage;
        private readonly ISaveStorage _cloudStorage;
        private readonly ISaveCodec _codec;
        private readonly IApplicationLifecycle _lifecycle;
        private readonly ITickService _tickService;
        private readonly IGameClock _clock;
        private readonly IPauseService _pauseService;

        private readonly SerialDisposable _pendingFlush = new();

        private List<ISaveParticipant> _participants;
        private SaveEnvelope _envelope = SaveEnvelope.CreateDefault();
        private SaveReason _pendingReason;
        private bool _isDirty;
        private bool _isWriting;
        private bool _hasPendingWrite;
        private bool _isRestored;
        private float _lastWriteTime = float.NegativeInfinity;

        public SaveService(
            [InjectOptional] LazyInject<List<ISaveParticipant>> participants,
            [InjectOptional] LazyInject<List<ISaveMigration>> migrations,
            [Inject(Id = SaveStorageId.LOCAL)] ISaveStorage localStorage,
            [Inject(Id = SaveStorageId.CLOUD)] ISaveStorage cloudStorage,
            ISaveCodec codec,
            IApplicationLifecycle lifecycle,
            ITickService tickService,
            IGameClock clock,
            IPauseService pauseService)
        {
            _pauseService = pauseService;
            _participantsSource = participants;
            _migrationsSource = migrations;
            _localStorage = localStorage;
            _cloudStorage = cloudStorage;
            _codec = codec;
            _lifecycle = lifecycle;
            _tickService = tickService;
            _clock = clock;
        }

        protected override void OnInitialize()
        {
            _pendingFlush.AddTo(Disposables);

            _lifecycle.Paused
                .Where(isPaused => isPaused)
                .Subscribe(_ => SaveNowAsync(includeCloud: true, DisposeToken).Forget())
                .AddTo(Disposables);

            _lifecycle.FocusChanged
                .Where(hasFocus => hasFocus == false)
                .Subscribe(_ => FlushIfDirty())
                .AddTo(Disposables);

            _pauseService.IsTimeFrozen
                .Where(isFrozen => isFrozen)
                .Subscribe(_ => FlushIfDirty())
                .AddTo(Disposables);

            _lifecycle.Quitting
                .Subscribe(_ => SaveNowAsync(includeCloud: true, CancellationToken.None).Forget())
                .AddTo(Disposables);

            _tickService.SecondTick
                .Subscribe(_ => TryAutoSave())
                .AddTo(Disposables);
        }

        private IReadOnlyList<ISaveParticipant> Participants
        {
            get
            {
                if (_participants != null)
                    return _participants;

                List<ISaveParticipant> source = _participantsSource?.Value;

                _participants = source == null
                    ? new List<ISaveParticipant>()
                    : source.OrderBy(participant => participant.Order).ToList();

                return _participants;
            }
        }

        public async UniTask LoadAsync(CancellationToken cancellationToken = default)
        {
            string localPayload = await _localStorage.ReadAsync(cancellationToken);
            SaveEnvelope local = Migrate(Deserialize(localPayload, _localStorage.Id));

            if (_cloudStorage == null || _cloudStorage.IsAvailable == false)
            {
                _envelope = local ?? SaveEnvelope.CreateDefault();
                return;
            }

            string cloudPayload = await _cloudStorage.ReadAsync(cancellationToken);
            SaveEnvelope cloud = Migrate(Deserialize(cloudPayload, _cloudStorage.Id));

            _envelope = ResolveConflict(local, cloud);
        }

        public void RestoreAll()
        {
            foreach (ISaveParticipant participant in Participants)
            {
                try
                {
                    participant.Restore(_envelope.GetSection(participant.Key));
                }
                catch (Exception exception)
                {
                    GameDebug.LogError(
                        $"[Save] Участник «{participant.Key}» не смог восстановиться: {exception.Message}");
                }
            }

            _isRestored = true;
            _isDirty = false;
            _pendingReason = SaveReason.Routine;
        }

        public void MarkDirty(SaveReason reason)
        {
            _isDirty = true;

            if (reason > _pendingReason)
                _pendingReason = reason;

            if (reason == SaveReason.Routine || _isRestored == false)
                return;

            ScheduleFlush();
        }

        public async UniTask SaveNowAsync(bool includeCloud, CancellationToken cancellationToken = default)
        {
            if (_isRestored == false)
                return;

            if (_isWriting)
            {
                _hasPendingWrite = true;
                return;
            }

            _isWriting = true;

            try
            {
                CaptureAll();

                _isDirty = false;
                _pendingReason = SaveReason.Routine;

                string payload = _codec.Encode(JsonConvert.SerializeObject(_envelope, Formatting.None));

                await _localStorage.WriteAsync(payload, cancellationToken);

                if (includeCloud && _cloudStorage != null && _cloudStorage.IsAvailable)
                    await _cloudStorage.WriteAsync(payload, cancellationToken);

                _lastWriteTime = Time.realtimeSinceStartup;
            }
            catch (OperationCanceledException)
            {
                _isDirty = true;
                throw;
            }
            catch (Exception exception)
            {
                _isDirty = true;
                GameDebug.LogError($"[Save] Запись не удалась: {exception.Message}");
            }
            finally
            {
                _isWriting = false;

                if (_hasPendingWrite)
                {
                    _hasPendingWrite = false;
                    ScheduleFlush();
                }
            }
        }

        private void TryAutoSave()
        {
            if (_isDirty == false || _isWriting)
                return;

            if (Time.realtimeSinceStartup - _lastWriteTime < AUTOSAVE_INTERVAL_SECONDS)
                return;

            SaveNowAsync(_pendingReason == SaveReason.Critical, DisposeToken).Forget();
        }

        private void FlushIfDirty()
        {
            if (_isDirty == false)
                return;

            SaveNowAsync(_pendingReason == SaveReason.Critical, DisposeToken).Forget();
        }

        private void ScheduleFlush()
        {
            _pendingFlush.Disposable = Observable.NextFrame()
                .Take(1)
                .Subscribe(_ =>
                {
                    if (_isDirty == false)
                        return;

                    bool includeCloud = _pendingReason == SaveReason.Critical;
                    SaveNowAsync(includeCloud, DisposeToken).Forget();
                });
        }

        private SaveEnvelope Migrate(SaveEnvelope envelope)
        {
            if (envelope == null)
                return null;

            if (envelope.Version > SaveEnvelope.CURRENT_VERSION)
            {
                GameDebug.LogError($"[Save] Сохранение версии {envelope.Version} новее поддерживаемой " +
                    $"{SaveEnvelope.CURRENT_VERSION}, читаем как есть.");
                return envelope;
            }

            while (envelope.Version < SaveEnvelope.CURRENT_VERSION)
            {
                ISaveMigration migration = FindMigration(envelope.Version);

                if (migration == null)
                {
                    GameDebug.LogError($"[Save] Нет миграции с версии {envelope.Version} на " +
                        $"{envelope.Version + 1}, дальше сохранение читается как есть.");
                    break;
                }

                try
                {
                    migration.Apply(envelope);
                }
                catch (Exception exception)
                {
                    GameDebug.LogError($"[Save] Миграция с версии {migration.FromVersion} упала: {exception.Message}");
                    break;
                }

                envelope.Version = migration.FromVersion + 1;
            }

            return envelope;
        }

        private ISaveMigration FindMigration(int fromVersion)
        {
            List<ISaveMigration> migrations = _migrationsSource?.Value;

            if (migrations == null)
                return null;

            foreach (ISaveMigration migration in migrations)
            {
                if (migration.FromVersion == fromVersion)
                    return migration;
            }

            return null;
        }

        private void CaptureAll()
        {
            foreach (ISaveParticipant participant in Participants)
            {
                try
                {
                    object data = participant.Capture();

                    if (data == null)
                        continue;

                    _envelope.Sections[participant.Key] = Newtonsoft.Json.Linq.JToken.FromObject(data);
                }
                catch (Exception exception)
                {
                    GameDebug.LogError($"[Save] Участник «{participant.Key}» не отдал снимок: {exception.Message}");
                }
            }

            _envelope.Version = SaveEnvelope.CURRENT_VERSION;
            _envelope.SavedAtUtc = _clock.UtcNow;
            _envelope.SaveCounter++;
        }

        private SaveEnvelope Deserialize(string payload, string storageId)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return null;

            if (_codec.TryDecode(payload, out string json) == false)
            {
                GameDebug.LogError($"[Save] Сохранение «{storageId}» не прочитано: формат не распознан.");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<SaveEnvelope>(json);
            }
            catch (JsonException exception)
            {
                GameDebug.LogError($"[Save] Сохранение «{storageId}» не разобрано: {exception.Message}");
                return null;
            }
        }

        private static SaveEnvelope ResolveConflict(SaveEnvelope local, SaveEnvelope cloud)
        {
            if (local == null && cloud == null)
                return SaveEnvelope.CreateDefault();

            if (local == null)
                return cloud;

            if (cloud == null)
                return local;

            if (cloud.SaveCounter != local.SaveCounter)
                return cloud.SaveCounter > local.SaveCounter ? cloud : local;

            return cloud.SavedAtUtc > local.SavedAtUtc ? cloud : local;
        }
    }
}
