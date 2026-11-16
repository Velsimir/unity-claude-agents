using System;

namespace {{ROOT}}.Core.Pause
{
    [Flags]
    public enum PauseEffect
    {
        None = 0,
        Time = 1,
        Audio = 2,
        Input = 4,
        Gameplay = 8,
        Ad = Time | Audio | Input | Gameplay,
    }
}
