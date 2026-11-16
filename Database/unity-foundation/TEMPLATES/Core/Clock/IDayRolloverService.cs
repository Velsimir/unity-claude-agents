using System;
using R3;

namespace {{ROOT}}.Core.Clock
{
    public interface IDayRolloverService
    {
        ReadOnlyReactiveProperty<int> CurrentDay { get; }

        DateTime NextRolloverUtc { get; }

        TimeSpan TimeUntilNextRollover { get; }

        int GetDay(DateTime utc);
    }
}
