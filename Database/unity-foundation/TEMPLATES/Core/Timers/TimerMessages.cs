using System;
using MessagePipe;
using Zenject;

namespace {{ROOT}}.Core.Timers
{
    public static class TimerMessages
    {
        public record Completed(string Id, DateTime EndUtc, TimeSpan Overdue, bool WasExpiredOffline);

        public static void Install(DiContainer container, MessagePipeOptions options)
        {
            container.BindMessageBroker<Completed>(options);
        }
    }
}
