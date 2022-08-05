using System.Data;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;
using MDictindle.Step;

namespace MDictindle;

public class DictManager : IDisposable
{
    private string DataBaseFile { get; }
    internal SQLiteConnection DataBaseConnection { get; }
    public string DictionaryFullPath { get; }
    public string DictionaryDirPath { get; }
    public string DictionaryName { get; }
    public string? CssContent { get; }

    private Dictionary<string, ISet<string>> InflMappings { get; } = new();

    // private readonly Dictionary<string, string> _reverseInflMappings = new();

    // private Dictionary<string, string> ReverseInflMappings
    // {
    //     get
    //     {
    //         if (_reverseInflMappings.Count != 0)
    //         {
    //             return _reverseInflMappings;
    //         }
    //
    //         foreach (var pair in InflMappings)
    //         {
    //             foreach (var s in pair.Value)
    //             {
    //                 _reverseInflMappings[s] = pair.Key;
    //             }
    //
    //             _reverseInflMappings[pair.Key] = pair.Key;
    //         }
    //
    //         return _reverseInflMappings;
    //     }
    // }

    public DictManager(string path2Dictionary, string? css)
    {
        CssContent = css is null ? null : File.ReadAllText(css);
        DataBaseFile = Path.GetTempFileName();
        DictionaryFullPath = Path.GetFullPath(path2Dictionary);
        DictionaryName = Path.GetFileNameWithoutExtension(path2Dictionary);
        DictionaryDirPath = Path.GetDirectoryName(path2Dictionary)!;

        SQLiteConnection.CreateFile(DataBaseFile);
        DataBaseConnection = new SQLiteConnection("Data Source=" + DataBaseFile);
        DataBaseConnection.Open();
        using var cmd =
            new SQLiteCommand(
                "CREATE TABLE Dictionary (" +
                // "NumId                 INTEGER           NOT NULL," +
                "Id                    TEXT PRIMARY KEY  NOT NULL, " +
                "EntryName             TEXT              NOT NULL, " +
                "Explanation           TEXT              NOT NULL, " +
                "Infls                 TEXT              NOT NULL, " +
                "IsPhrase              SMALLINT          NOT NULL" +
                ")",
                DataBaseConnection);
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX NameAndIdIndex ON Dictionary (EntryName, Id)";
        cmd.ExecuteNonQuery();
    }

    ~DictManager()
    {
        Dispose(false);
    }

    private static string ReadId(string name, string explanation)
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

        id = Utils.GetId(id);

        return id;
    }

    private uint _count;
    public uint GetCount() => _count;

    private const string CmdStrForAddingEntry =
        @"INSERT INTO Dictionary(Id, EntryName, Explanation, Infls, IsPhrase) VALUES (@id, @name, @explanation, '', @phrase)";

    private SQLiteCommand? _cmdForAddingEntry;

    private SQLiteCommand CmdForAddingEntry =>
        _cmdForAddingEntry ??= new SQLiteCommand(CmdStrForAddingEntry, DataBaseConnection);

    private static readonly Regex RegexForGettingInfl = new(@"@@@LINK=(.*?)(<br/>)?\\?n?$", RegexOptions.Compiled);

    public async Task AddEntryAsync(string name, string explanation)
    {
        if (!explanation.StartsWith("@@@LINK="))
        {
            // var count = _count;
            var id = ReadId(name, explanation);
            CmdForAddingEntry.Reset();
            // CmdForAddingEntry.Parameters.Add("@index", DbType.UInt32).Value = count;
            CmdForAddingEntry.Parameters.Add("@id", DbType.String).Value = id;
            CmdForAddingEntry.Parameters.Add("@name", DbType.String).Value = name;
            CmdForAddingEntry.Parameters.Add("@explanation", DbType.String).Value = explanation;
            CmdForAddingEntry.Parameters.Add("@phrase", DbType.Boolean).Value = name.Contains(' ');
            await CmdForAddingEntry.PrepareAsync();
            await CmdForAddingEntry.ExecuteNonQueryAsync();
            _count++;
        }
        else
        {
            // 8 == "@@@LINK=".Length
            var sourceWord = RegexForGettingInfl.Match(explanation).Groups[1].Value;
            if (!InflMappings.ContainsKey(sourceWord))
            {
                InflMappings[sourceWord] = new HashSet<string>();
            }

            InflMappings[sourceWord].Add(name);
        }
    }

    private const string CmdStrForAddingEntryForInfl =
        @"INSERT INTO Dictionary(Id, EntryName, Explanation, Infls, IsPhrase) VALUES (@id, @name, @explanation, @infls, @phrase)";

    private SQLiteCommand? _cmdForAdding;

    private SQLiteCommand CmdForAdding =>
        _cmdForAdding ??= new SQLiteCommand(CmdStrForAddingEntryForInfl, DataBaseConnection);

    private async Task AddEntryForInflAsync(string name, string explanation, string infls)
    {
        // await using var cmd = DataBaseConnection.CreateCommand();
        var count = _count;

        // CmdForSelect.Reset();
        // CmdForSelect.Parameters.Add("@index", DbType.UInt32).Value = sourceIndex;
        // await using var reader = await CmdForSelect.ExecuteReaderAsync();
        // await reader.ReadAsync();
        // var name = reader.GetString(0);
        // var explanation = reader.GetString(1);

        CmdForAdding.Reset();
        // CmdForAdding.Parameters.Add("@index", DbType.UInt32).Value = count;
        // 'c' for count
        CmdForAdding.Parameters.Add("@id", DbType.String).Value = count + 'c';
        CmdForAdding.Parameters.Add("@name", DbType.String).Value = name;
        CmdForAdding.Parameters.Add("@explanation", DbType.String).Value = explanation;
        CmdForAdding.Parameters.Add("@infls", DbType.String).Value = infls;
        // 这个字段本身就是为了给短语添加变形而出现的，对于已经添加了变形的短语不必再使用这个字段
        // 如果置 true 会导致错误地被识别为没有处理过的短语
        CmdForAdding.Parameters.Add("@phrase", DbType.Boolean).Value = false;
        await CmdForAdding.PrepareAsync();
        await CmdForAdding.ExecuteNonQueryAsync();

        _count++;
    }

    public async Task MakeInflsAsync()
    {
        await using var tran = DataBaseConnection.BeginTransaction();
        await using var cmd = DataBaseConnection.CreateCommand();
        cmd.CommandText = "UPDATE Dictionary SET Infls = @infl WHERE EntryName = @name";
        foreach (var (source, value) in InflMappings)
        {
            cmd.Reset();
            var inflString = string.Join(',', value);
            cmd.Parameters.Add("@infl", DbType.String).Value = inflString;
            cmd.Parameters.Add("@name", DbType.String).Value = source;
            await cmd.PrepareAsync();
            await cmd.ExecuteNonQueryAsync();
        }

        cmd.CommandText = "SELECT Id, EntryName, Explanation FROM Dictionary WHERE IsPhrase = 1";
        await using var reader = cmd.ExecuteReader();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1)!;
            var explanation = reader.GetString(2);
            var words = name.Split(' ');
            var listInfls = new List<ISet<string>>();
            await using var cmd2 = DataBaseConnection.CreateCommand();
            cmd2.CommandText = "SELECT Infls FROM Dictionary WHERE EntryName = @name LIMIT 1";

            foreach (var word in words)
            {
                cmd2.Reset();
                cmd2.Parameters.Add("@name", DbType.String).Value = word;
                await cmd2.PrepareAsync();
                await using var tmpReader = await cmd2.ExecuteReaderAsync();
                var infls = await tmpReader.ReadAsync() ? tmpReader.GetString(0) + $",{word}" : word;
                listInfls.Add(infls.Split(',')
                    .Where(it =>
                        // 1.5x 原长度 + 2<=变形词长度；变形词是单词而非短语
                        word.Length * 3 / 2 + 2 <= it.Length && it.All(c => c != ' ' && c != '\'' && c != '"' && c != '-'))
                    .ToHashSet());
            }

            var res = Utils.GetPermutationAndCombination(listInfls);
            var finalPhrases = res
                .Select(list => string.Join(' ', list))
                .Where(it => it.Count(c => c == ' ') < 4)
                .ToList();
            finalPhrases.Remove(name);
            var sb = new StringBuilder();
            var i = 0;
            foreach (var phrase in finalPhrases)
            {
                sb.Append(phrase);
                sb.Append(',');
                i++;
                if (i < 255) continue;
                i = 0;
                sb.Remove(sb.Length - 1, 1);
                await AddEntryForInflAsync(name, explanation, sb.ToString());
                sb.Clear();
            }

            if (i == 0) continue;
            var inflString = sb[^1] != ',' ? sb.ToString() : sb.Remove(sb.Length - 1, 1).ToString();
            cmd2.Reset();
            cmd2.CommandText = "UPDATE Dictionary SET Infls = @infl WHERE Id = @id";
            cmd2.Parameters.Add("@infl", DbType.String).Value = inflString;
            cmd2.Parameters.Add("@id", DbType.String).Value = id;
            await cmd2.PrepareAsync();
            await cmd2.ExecuteNonQueryAsync();
        }

        await using var cmd4 = DataBaseConnection.CreateCommand();
        cmd4.CommandText = "SELECT Id, EntryName, Explanation, Infls FROM Dictionary WHERE IsPhrase = 0";
        await using var reader2 = cmd4.ExecuteReader();
        await using var cmd3 = DataBaseConnection.CreateCommand();
        cmd3.CommandText = "UPDATE Dictionary SET Infls = @infl WHERE Id = @id";
        while (await reader2.ReadAsync())
        {
            var id = reader2.GetString(0);
            var name = reader2.GetString(1)!;
            var explanation = reader2.GetString(2);
            var infls = reader2.GetString(2);
            var inflCount = infls.Length - infls.Replace(",", "").Length + 1;
            if (inflCount <= 255)
            {
                continue;
            }

            var listInfls = infls.Split(',');

            var sb = new StringBuilder();
            var i = 0;
            foreach (var infl in listInfls)
            {
                sb.Append(infl);
                sb.Append(',');
                i++;
                if (i < 255) continue;
                i = 0;
                sb.Remove(sb.Length - 1, 1);
                await AddEntryForInflAsync(name, explanation, sb.ToString());
                sb.Clear();
            }

            var inflString = sb[^1] != ',' ? sb.ToString() : sb.Remove(sb.Length - 1, 1).ToString();
            cmd3.Reset();
            cmd3.CommandText = "UPDATE Dictionary SET Infls = @infl WHERE Id = @id";
            cmd3.Parameters.Add("@infl", DbType.String).Value = inflString;
            cmd3.Parameters.Add("@id", DbType.String).Value = id;
            await cmd3.PrepareAsync();
            await cmd3.ExecuteNonQueryAsync();
        }

        await tran.CommitAsync();
    }

    // private SQLiteCommand? _cmdForGetEntry;
    // private SQLiteCommand CmdForGetEntry =>
    //     _cmdForGetEntry ??= new SQLiteCommand(
    //         "SELECT NumId FROM Dictionary WHERE @str = Id " +
    //         "LIMIT 1", DataBaseConnection
    //     );

    private SQLiteCommand? _cmdForCheckIdExists;

    private SQLiteCommand CmdForCheckIdExists =>
        _cmdForCheckIdExists ??= new SQLiteCommand(
            "SELECT EXISTS( SELECT 1 FROM Dictionary WHERE Id = @str LIMIT 1 ) ", DataBaseConnection
        );

    public async Task<bool> ContainsId(string id)
    {
        id = Utils.GetId(id);

        CmdForCheckIdExists.Reset();
        CmdForCheckIdExists.Parameters.Add("str", DbType.String).Value = id;
        await CmdForCheckIdExists.PrepareAsync();
        var reader = await CmdForCheckIdExists.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetBoolean(0);
    }

    // public async Task<uint?> GetFirstEntryOrNullByIdAsync(string str)
    // {
    //     // if (ReverseInflMappings.ContainsKey(str))
    //     // {
    //     //     str = ReverseInflMappings[str];
    //     // }
    //     //
    //     if (str.Contains('@') || str.Contains('=') || str.Contains('&'))
    //     {
    //         // 'h' for HashCode
    //         str = str.GetHashCode() + "h";
    //     }
    //     
    //     CmdForGetEntry.Reset();
    //     CmdForGetEntry.Parameters.Add("str", DbType.String).Value = str;
    //     await CmdForGetEntry.PrepareAsync();
    //     var reader = await CmdForGetEntry.ExecuteReaderAsync();
    //     if (!await reader.ReadAsync()) return null;
    //     return (uint)reader.GetInt64(0);
    // }

    private void Dispose(bool disposing)
    {
        if (!disposing) return;
        _cmdForAddingEntry?.Dispose();
        _cmdForAdding?.Dispose();
        _cmdForCheckIdExists?.Dispose();
        DataBaseConnection.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Thread.Sleep(1000);
        File.Delete(DataBaseFile);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}