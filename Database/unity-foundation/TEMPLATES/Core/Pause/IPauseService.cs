using System;
using R3;

namespace {{ROOT}}.Core.Pause
{
    public interface IPauseService
    {
        ReadOnlyReactiveProperty<bool> IsTimeFrozen { get; }
        ReadOnlyReactiveProperty<bool> IsAudioMuted { get; }
        ReadOnlyReactiveProperty<bool> IsInputBlocked { get; }
        ReadOnlyReactiveProperty<bool> IsGameplayBlocked { get; }

        IDisposable Acquire(PauseEffect effects);

        void ReleaseAll();
    }
}
