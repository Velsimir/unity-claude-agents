namespace {{ROOT}}.Core.Save
{
    public interface ISaveCodec
    {
        bool IsEncrypting { get; }

        string Encode(string json);

        bool TryDecode(string payload, out string json);
    }
}
