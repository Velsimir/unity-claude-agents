using Newtonsoft.Json.Linq;

namespace {{ROOT}}.Core.Save
{
    public interface ISaveParticipant
    {
        string Key { get; }

        int Order { get; }

        object Capture();

        void Restore(JToken section);
    }
}
