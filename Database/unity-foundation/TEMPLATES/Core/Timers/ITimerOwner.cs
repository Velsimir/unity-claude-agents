namespace {{ROOT}}.Core.Timers
{
    public interface ITimerOwner
    {
        string TimerPrefix { get; }

        void OnTimerCompleted(TimerMessages.Completed completion);
    }
}
