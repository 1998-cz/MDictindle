using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepOne : AbsStep
{
    public override string Description => "读取词典到内存";
    public override bool EnableAsync => true;

    public override void Do(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第一步请使用 Async");
    }

    public override async Task DoAsync(DictManager manager, TextWriter logger)
    {
        await using var stream = File.OpenRead(manager.DictionaryFullPath);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is { } l)
        {
            // 空行
            if (Regex.IsMatch(l, @"^\s*$"))
            {
                continue;
            }

            var split = l.Split('\t');
            var name = split[0];
            var explanation = split[1].Replace("\\\\", "\\").Replace("\\n", "<br/>\n");
            manager.AddEntry(name, explanation);
        }

        GC.Collect();
        await logger.WriteLineAsync("第一步：读取完成，正在处理变形词...");
        manager.CreateIdMappingsAndMakeInfls();
    }
}