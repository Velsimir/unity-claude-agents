using System;
using MessagePipe;
using Zenject;

namespace {{ROOT}}.Core.Clock
{
    public static class DayRolloverMessages
    {
        public record DayChanged(int Day, int DaysPassed, bool WasOffline, DateTime DayStartUtc);

        public static void Install(DiContainer container, MessagePipeOptions options)
        {
            container.BindMessageBroker<DayChanged>(options);
        }
    }
}
