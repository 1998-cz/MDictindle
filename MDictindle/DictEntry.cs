namespace MDictindle;

public class DictEntry
{
    public string WordOrPhrase { get; }
    public string Id { get; }
    public string Ket => WordOrPhrase.Normalize();
    public ISet<DictEntry> Infls { get; } = new HashSet<DictEntry>();
    public DictEntry? SourceEntry { get; } = null;

    public DictEntry(string wordOrPhrase, string explanation)
    {
        WordOrPhrase = wordOrPhrase;
    }
}