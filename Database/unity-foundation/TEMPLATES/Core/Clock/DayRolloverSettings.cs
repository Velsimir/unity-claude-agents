namespace {{ROOT}}.Core.Clock
{
    public sealed class DayRolloverSettings
    {
        public DayRolloverSettings(int utcOffsetMinutes)
        {
            UtcOffsetMinutes = utcOffsetMinutes;
        }

        public int UtcOffsetMinutes { get; }
    }
}
