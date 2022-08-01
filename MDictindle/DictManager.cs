using System.Collections.Specialized;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using MDictindle.Step;

namespace MDictindle;

public class DictManager
{
    internal string DataBaseFile { get; }
    internal SQLiteConnection DataBaseConnection { get; }
    public string DictionaryFullPath { get; }
    public string DictionaryDirPath { get; }
    public string DictionaryName { get; }

    public uint EntryNumber { get; private set; }

    public LinkedList<DictEntry> Entries { get; } = new();

    /// <summary>
    /// id:string -> entry:DictEntry
    /// </summary>
    private HybridDictionary IdMappings { get; } = new();

    // name:string -> entries:ISet<DictEntry>
    private HybridDictionary NameMappings { get; } = new();

    private Dictionary<string, ISet<string>> InflMappings { get; } = new();

    private readonly Dictionary<string, string> _reverseInflMappings = new();

    private Dictionary<string, string> ReverseInflMappings
    {
        get
        {
            if (_reverseInflMappings.Count != 0)
            {
                return _reverseInflMappings;
            }

            foreach (var pair in InflMappings)
            {
                foreach (var s in pair.Value)
                {
                    _reverseInflMappings[s] = pair.Key;
                }

                _reverseInflMappings[pair.Key] = pair.Key;
            }

            return _reverseInflMappings;
        }
    }

    public DictManager(string path2Dictionary)
    {
        DataBaseFile = Path.GetTempFileName();
        DictionaryFullPath = Path.GetFullPath(path2Dictionary);
        DictionaryName = Path.GetFileNameWithoutExtension(path2Dictionary);
        DictionaryDirPath = Path.GetDirectoryName(path2Dictionary)!;
        
        SQLiteConnection.CreateFile(DataBaseFile);
        DataBaseConnection = new SQLiteConnection("Data Source=" + DataBaseFile);
        using var cmd = new SQLiteCommand("CREATE TABLE Dictionary(Index INTEGER, Id TEXT, Name TEXT, Explanation TEXT)", DataBaseConnection);
        cmd.ExecuteNonQuery();
    }

    ~DictManager()
    {
        DataBaseConnection.Close();
        File.Delete(DataBaseFile);
    }

    private string ReadId(string name, string explanation)
    {
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

        return id;
    }

    public void AddEntry(string name, string explanation)
    {
        if (!explanation.StartsWith("@@@LINK="))
        {
            var seekIndex = new FileInfo(DataBaseFile).Length;
            var stream = File.AppendText(DataBaseFile);
            stream.Write(explanation);
            stream.Flush();
            stream.Close();
            
            var entry = new DictEntry(name, seekIndex, EntryNumber, this);
            Entries.AddLast(entry);
            EntryNumber++;
            if (!NameMappings.Contains(name))
            {
                NameMappings[name] = new HashSet<DictEntry>();
            }

            ((HashSet<DictEntry>)NameMappings[name]!).Add(entry);
        }

        // 8 == "@@@LINK=".Length
        var sourceWord = Regex.Match(explanation, @"@@@LINK=(.*?)(<br/>)?\\?n?$").Groups[1].Value;
        if (!InflMappings.ContainsKey(sourceWord))
        {
            InflMappings[sourceWord] = new HashSet<string>();
        }

        InflMappings[sourceWord].Add(name);
    }

    private void AddEntryForInfl(DictEntry source)
    {
        Entries.AddLast(new DictEntry(source, EntryNumber));
        EntryNumber++;
    }

    public void CreateIdMappingsAndMakeInfls()
    {
        foreach (var entry in Entries)
        {
            var id = entry.Id;
            IdMappings[id] = entry;
        }

        var sourceMapping = GetEntriesByNames(InflMappings.Keys);
        foreach (var pair in sourceMapping)
        {
            var entry = pair.Key;
            var infls = InflMappings[entry.Name];
            foreach (var infl in infls)
            {
                entry.Infls.Add(infl);
            }
        }

        // 下面为词组添加变形词
        foreach (var entry in Entries.Where(it => it.IsPhrase).ToList())
        {
            var words = entry.Name.Split(' ');
            var listInfls = new List<ISet<string>>();
            foreach (var word in words)
            {
                var wordEntry = GetFirstEntryOrNullByName(word);
                listInfls.Add(wordEntry is null
                    ? new HashSet<string>()
                    : wordEntry.Infls.ToHashSet());
                listInfls.Last().Add(word);
            }

            var res = Utils.GetPermutationAndCombination(listInfls);
            var finalPhrases = res.Select(list => string.Join(' ', list)).ToList();
            var length = 1 + (finalPhrases.Count - 1) / 255;
            // 因为使用下面的算法 arr[0] 最多储存 254 个
            if (finalPhrases.Count % 255 == 0) length++;
            var arr = new List<string>[length];
            for (var i = 0; i < finalPhrases.Count; i++)
            {
                if ((i + 1) % 255 == 0 || i == 0)
                {
                    arr[(i + 1) / 255] = new List<string>();
                }

                arr[(i + 1) / 255].Add(finalPhrases[i]);
            }

            var nowEntry = entry;
            for (var i = 0; i < arr.Length; i++)
            {
                if (i != 0)
                {
                    AddEntryForInfl(entry);
                    nowEntry = Entries.Last();
                }

                foreach (var s in arr[i])
                {
                    nowEntry.Infls.Add(s);
                }
            }
        }
    }

    private Dictionary<DictEntry, string> GetEntriesByNames(ICollection<string> names)
    {
        var relatedEntries = Entries.Where(it => names.Contains(it.Name));

        return relatedEntries.ToDictionary(relatedEntry => relatedEntry, relatedEntry => relatedEntry.Name);
    }

    private DictEntry? GetFirstEntryOrNullByName(string name) =>
        NameMappings.Contains(name) ? ((HashSet<DictEntry>)NameMappings[name]!).First() : null;

    public DictEntry? GetFirstEntryOrNullByNameOrInflOrId(string str) =>
        GetFirstEntryOrNullByName(str) ??
        (IdMappings.Contains(str)
            ? (DictEntry)IdMappings[str]!
            : ReverseInflMappings.ContainsKey(str)
                ? GetFirstEntryOrNullByName(ReverseInflMappings[str])
                : null);

    private bool ContainsName(string name) => NameMappings.Contains(name);
    private bool ContainsId(string name) => IdMappings.Contains(name);

    public bool ContainsNameOrInflOrId(string str) =>
        ContainsId(str) || ContainsName(str) || ReverseInflMappings.ContainsKey(str);
}