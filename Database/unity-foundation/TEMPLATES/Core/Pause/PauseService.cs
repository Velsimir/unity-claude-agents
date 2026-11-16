using System;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using {{ROOT}}.Core.Common;
using {{ROOT}}.Platform;

namespace {{ROOT}}.Core.Pause
{
    public class PauseService : Service, IPauseService
    {
        private const float PAUSED_TIME_SCALE = 0f;
        private const float DEFAULT_TIME_SCALE = 1f;
        private const int TIME_INDEX = 0;
        private const int AUDIO_INDEX = 1;
        private const int INPUT_INDEX = 2;
        private const int GAMEPLAY_INDEX = 3;
        private const int EFFECTS_COUNT = 4;

        private static readonly PauseEffect[] SingleEffects =
        {
            PauseEffect.Time,
            PauseEffect.Audio,
            PauseEffect.Input,
            PauseEffect.Gameplay,
        };

        private readonly IPlatformSdk _platformSdk;
        private readonly IApplicationLifecycle _lifecycle;

        private readonly int[] _counts = new int[EFFECTS_COUNT];
        private readonly ReactiveProperty<bool> _isTimeFrozen = new(false);
        private readonly ReactiveProperty<bool> _isAudioMuted = new(false);
        private readonly ReactiveProperty<bool> _isInputBlocked = new(false);
        private readonly ReactiveProperty<bool> _isGameplayBlocked = new(false);

        private IDisposable _suspendToken;
        private EventSystem _blockedEventSystem;
        private float _savedTimeScale = DEFAULT_TIME_SCALE;
        private int _generation;
        private bool _isApplicationPaused;
        private bool _hasFocus = true;

        public PauseService(IPlatformSdk platformSdk, IApplicationLifecycle lifecycle)
        {
            _platformSdk = platformSdk;
            _lifecycle = lifecycle;
        }

        public ReadOnlyReactiveProperty<bool> IsTimeFrozen => _isTimeFrozen;
        public ReadOnlyReactiveProperty<bool> IsAudioMuted => _isAudioMuted;
        public ReadOnlyReactiveProperty<bool> IsInputBlocked => _isInputBlocked;
        public ReadOnlyReactiveProperty<bool> IsGameplayBlocked => _isGameplayBlocked;

        protected override void OnInitialize()
        {
            _isTimeFrozen.AddTo(Disposables);
            _isAudioMuted.AddTo(Disposables);
            _isInputBlocked.AddTo(Disposables);
            _isGameplayBlocked.AddTo(Disposables);

            _lifecycle.Paused
                .Subscribe(isPaused =>
                {
                    _isApplicationPaused = isPaused;
                    RefreshSuspendMute();
                })
                .AddTo(Disposables);

            _lifecycle.FocusChanged
                .Subscribe(hasFocus =>
                {
                    _hasFocus = hasFocus;
                    RefreshSuspendMute();
                })
                .AddTo(Disposables);
        }

        protected override void OnDispose()
        {
            ReleaseEverything();
        }

        public IDisposable Acquire(PauseEffect effects)
        {
            for (int i = 0; i < SingleEffects.Length; i++)
            {
                if ((effects & SingleEffects[i]) != 0)
                    Increment(i);
            }

            return new Token(this, effects, _generation);
        }

        public void ReleaseAll()
        {
            ReleaseEverything();
            RefreshSuspendMute();
        }

        private void ReleaseEverything()
        {
            _generation++;
            _suspendToken = null;

            for (int i = 0; i < SingleEffects.Length; i++)
            {
                if (_counts[i] == 0)
                    continue;

                _counts[i] = 0;
                Apply(i, false);
            }

            RefreshFlags();
        }

        private void Release(PauseEffect effects, int generation)
        {
            if (generation != _generation)
                return;

            for (int i = 0; i < SingleEffects.Length; i++)
            {
                if ((effects & SingleEffects[i]) != 0)
                    Decrement(i);
            }
        }

        private void Increment(int index)
        {
            _counts[index]++;

            if (_counts[index] == 1)
                Apply(index, true);

            RefreshFlags();
        }

        private void Decrement(int index)
        {
            if (_counts[index] == 0)
                return;

            _counts[index]--;

            if (_counts[index] == 0)
                Apply(index, false);

            RefreshFlags();
        }

        private void Apply(int index, bool isActive)
        {
            switch (index)
            {
                case TIME_INDEX:
                    ApplyTime(isActive);
                    break;

                case AUDIO_INDEX:
                    AudioListener.pause = isActive;
                    break;

                case INPUT_INDEX:
                    ApplyInput(isActive);
                    break;

                case GAMEPLAY_INDEX:
                    ApplyGameplay(isActive);
                    break;
            }
        }

        private void ApplyTime(bool isFrozen)
        {
            if (isFrozen)
            {
                _savedTimeScale = Time.timeScale > PAUSED_TIME_SCALE ? Time.timeScale : DEFAULT_TIME_SCALE;
                Time.timeScale = PAUSED_TIME_SCALE;
                return;
            }

            Time.timeScale = _savedTimeScale;
        }

        private void ApplyInput(bool isBlocked)
        {
            if (isBlocked)
            {
                EventSystem current = EventSystem.current;

                if (current == null)
                {
                    GameDebug.LogError("[Pause] EventSystem не найден, ввод не заблокирован.");
                }
                else
                {
                    _blockedEventSystem = current;
                    current.enabled = false;
                }
            }
            else if (_blockedEventSystem != null)
            {
                _blockedEventSystem.enabled = true;
                _blockedEventSystem = null;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = isBlocked == false;
#endif
        }

        private void ApplyGameplay(bool isBlocked)
        {
            if (isBlocked)
                _platformSdk.Gameplay.GameplayStopped();
            else
                _platformSdk.Gameplay.GameplayStarted();
        }

        private void RefreshFlags()
        {
            _isTimeFrozen.Value = _counts[TIME_INDEX] > 0;
            _isAudioMuted.Value = _counts[AUDIO_INDEX] > 0;
            _isInputBlocked.Value = _counts[INPUT_INDEX] > 0;
            _isGameplayBlocked.Value = _counts[GAMEPLAY_INDEX] > 0;
        }

        private void RefreshSuspendMute()
        {
            bool isSuspended = _isApplicationPaused || _hasFocus == false;

            if (isSuspended && _suspendToken == null)
            {
                _suspendToken = Acquire(PauseEffect.Audio);
                return;
            }

            if (isSuspended == false && _suspendToken != null)
            {
                _suspendToken.Dispose();
                _suspendToken = null;
            }
        }

        private sealed class Token : IDisposable
        {
            private readonly PauseService _owner;
            private readonly int _generation;

            private PauseEffect _effects;

            public Token(PauseService owner, PauseEffect effects, int generation)
            {
                _owner = owner;
                _effects = effects;
                _generation = generation;
            }

            public void Dispose()
            {
                if (_effects == PauseEffect.None)
                    return;

                PauseEffect effects = _effects;
                _effects = PauseEffect.None;

                _owner.Release(effects, _generation);
            }
        }
    }
}
