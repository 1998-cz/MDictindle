using System.Text;
using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepTwo : AbsStep
{
    public override string Description => "处理链接";
    public override bool EnableAsync => false;

    public override void Do(DictManager manager, TextWriter logger)
    {
        logger.WriteLine($"第二步：共有 {manager.EntryNumber} 个条目需要处理，请耐心。除最前面的 2000 条目和最后的 2000 条目外，每个条目所花费的时间大致相同");
        uint i = 0;
        foreach (var entry in manager.Entries)
        {
            i++;
            var explanation = File.ReadAllText(entry.ExplanationFilePath);
            var newExplanation = new StringBuilder(explanation);
            const string pattern = "(?<!text/css\" )href=\"(bword://(\\S+?))\"";
            var collection = Regex.Matches(explanation, pattern);
            foreach (Match match in collection)
            {
                var linked = match.Groups[2].Value;
                if (!linked.Contains('#'))
                {
                    var source = manager
                        .GetFirstEntryOrNullByNameOrInflOrId(linked);

                    if (source is null)
                    {
                        logger.WriteLine($"第二步：警告：存在指向 {linked} 的链接，但 {linked} 不存在");
                        continue;
                    }

                    var id = source.Id;
                    var location = source.Index / 2000;
                    newExplanation.Replace(match.Value,
                        $"<a href=\"{manager.DictionaryName}{location}.html#{id}\">");
                }
                else
                {
                    var split = linked.Split('#');
                    var prefix = split[0];
                    var postfix = split[1];
                    string? id = null;
                    uint? index = null;
                    // 优先使用后缀
                    if (manager.ContainsNameOrInflOrId(postfix))
                    {
                        var postfixEntry = manager
                            .GetFirstEntryOrNullByNameOrInflOrId(postfix);

                        id = postfixEntry?.Id;
                        index = postfixEntry?.Index;
                    }
                    else if (manager.ContainsNameOrInflOrId(prefix))
                    {
                        var prefixEntry = manager
                            .GetFirstEntryOrNullByNameOrInflOrId(prefix);
                        id = prefixEntry?.Id;
                        index = prefixEntry?.Index;
                    }

                    if (id is null)
                    {
                        logger.WriteLine($"第二步：警告：存在指向 {linked} 的链接，但 {linked} 不存在");
                        continue;
                    }

                    var location = index! / 2000;
                    newExplanation.Replace(match.Value,
                        $"<a href=\"{manager.DictionaryName}{location}.html#{id}\">");
                }
            }

            File.WriteAllText(entry.ExplanationFilePath, newExplanation.ToString());
            if (i % 2000 == 0)
            {
                logger.WriteLine($"第二步：已经处理了 {i} 个条目");
            }
        }
        GC.Collect();
    }

    public override Task DoAsync(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第二步不支持 Async");
    }
}