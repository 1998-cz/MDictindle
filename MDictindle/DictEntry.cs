using System.Text;

namespace MDictindle;

public class DictEntry
{
    public string Name { get; }

    public string ExplanationFilePath => Manager.DataBaseFile;
    internal long SeekIndex { get; }

    public bool IsPhrase => Name.Contains(' ');

    public string Id { get; }
    public string Key => Name.Normalize();
    public ISet<string> Infls { get; } = new HashSet<string>();

    public DictManager Manager { get; }
    public uint Index { get; }

    internal DictEntry(DictEntry entry, uint index)
    {
        Name = entry.Name;
        Manager = entry.Manager;
        // 他不需要有意义的 ID
        Id = entry.Id + Manager.EntryNumber;
        ExplanationFilePath = entry.ExplanationFilePath;
        Index = index;
    }

    internal DictEntry(string name, long seekIndex, uint index, DictManager manager)
    {
        Name = name;
        Manager = manager;
        SeekIndex = seekIndex;
        Index = index;

        var explanation = File.ReadAllText(explanationFilePath);

        // // 检查是否为某个词语的变形，如果是，那么 id 置 null
        // if (explanation.StartsWith("@@@LINK="))
        // {
        //     // 7 == "@@@LINK=".Length
        //     WordOfSourceEntry = explanation[7..];
        // }
        // // 读取 id
        // else
        // {
        const string pattern =
            "<div class=\"entry\"";
        var pos = explanation.IndexOf(pattern, StringComparison.Ordinal);
        var id = name;
        if (pos != -1)
        {
            var start = explanation.IndexOf("id=\"", pos + pattern.Length, StringComparison.Ordinal);
            // 4 == "id=\"".Length
            var end = explanation.IndexOf('"', start + 4);
            id = explanation[(start + 4)..end];
        }

        // 如果存在特殊字符，使用 hashcode 作为 id
        if (id.Contains('@') || id.Contains('=') || id.Contains('&'))
        {
            // 'h' for HashCode
            id = id.GetHashCode() + "h";
        }

        Id = id;
        // }
    }

    public string GetInflString()
    {
        if (Infls.Count > 255)
        {
            throw new Exception("Infls > 255");
        }

        if (Infls.Count == 0)
        {
            return "";
        }
        
        const string front = "<idx:infl>";
        const string end = "</idx:infl>";
        var sb = new StringBuilder(front);
        foreach (var infl in Infls)
        {
            sb.Append($"<idx:iform name=\"\" value=\"{infl}\" />");
        }

        sb.Append(end);
        return sb.ToString();
    }
}