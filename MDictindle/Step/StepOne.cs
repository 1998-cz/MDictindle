using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepOne : AbsStep
{
    public override string Description => "读取词典到数据库";
    public override bool EnableAsync => true;

    public override void Do(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第一步请使用 Async");
    }

    public override async Task DoAsync(DictManager manager, TextWriter logger)
    {
        await using var stream = File.OpenRead(manager.DictionaryFullPath);
        using var reader = new StreamReader(stream);
        await using var cmd = manager.DataBaseConnection.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous = OFF; PRAGMA journal_mode=OFF; ";
        cmd.ExecuteNonQuery();
        await using var tran = manager.DataBaseConnection.BeginTransaction();
        var re = new Regex(@"^\s*$", RegexOptions.Compiled);
        while (await reader.ReadLineAsync() is { } l)
        {
            // 空行
            if (re.IsMatch(l))
            {
                continue;
            }

            var split = l.Split('\t');
            var name = split[0];
            var explanation = split[1].Replace("\\\\", "\\").Replace("\\n", "<br/>");
            await manager.AddEntryAsync(name, explanation);
        }

        await tran.CommitAsync();

        GC.Collect();
        await logger.WriteLineAsync("第一步：读取完成，正在处理变形词...");
        await manager.MakeInflsAsync();
    }
}