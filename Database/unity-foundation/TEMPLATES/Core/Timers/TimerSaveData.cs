using System.Collections.Generic;

namespace {{ROOT}}.Core.Timers
{
    public class TimerSaveData
    {
        public List<TimerSaveEntry> Timers = new();
    }

    public class TimerSaveEntry
    {
        public string Id;
        public long EndUtcTicks;
    }
}
