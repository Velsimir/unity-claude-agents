namespace {{ROOT}}.Core.Save
{
    public interface ISaveMigration
    {
        int FromVersion { get; }

        void Apply(SaveEnvelope envelope);
    }
}
