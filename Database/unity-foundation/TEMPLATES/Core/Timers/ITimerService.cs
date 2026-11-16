using System;

namespace {{ROOT}}.Core.Timers
{
    public interface ITimerService
    {
        bool IsRunning(string id);

        TimeSpan GetRemaining(string id);

        bool TryGetEndUtc(string id, out DateTime endUtc);

        bool TryStart(string id, TimeSpan duration);

        bool Reschedule(string id, TimeSpan duration);

        bool Cancel(string id);

        bool Skip(string id);
    }
}
