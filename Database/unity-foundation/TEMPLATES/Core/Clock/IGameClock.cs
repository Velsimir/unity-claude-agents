using System;

namespace {{ROOT}}.Core.Clock
{
    public interface IGameClock
    {
        DateTime UtcNow { get; }

        bool IsTrusted { get; }

        TimeSpan GetElapsedSince(long utcTicks, TimeSpan maxElapsed);
    }
}
