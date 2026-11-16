using System;
using UnityEngine;
using Zenject;
using {{ROOT}}.Core.Common;

namespace {{ROOT}}.Core.Pause
{
    [DisallowMultipleComponent]
    public class PauseScope : MonoBehaviour
    {
        [SerializeField] private PauseEffect _effects = PauseEffect.Gameplay;

        private IPauseService _pauseService;
        private IDisposable _token;

        [Inject]
        private void Construct(IPauseService pauseService)
        {
            _pauseService = pauseService;
        }

        private void OnEnable()
        {
            if (_pauseService == null)
            {
                GameDebug.LogError("[PauseScope] Объект создан не через контейнер, пауза не взята.", this);
                return;
            }

            _token ??= _pauseService.Acquire(_effects);
        }

        private void OnDisable()
        {
            _token?.Dispose();
            _token = null;
        }
    }
}
