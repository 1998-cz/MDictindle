using System.Text;
using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepZero : AbsStep
{
    public override string Description => "清除图片、音乐、JS、无效链接等 Kindle 上无法正常使用的功能";
    public override bool EnableAsync => false;

    public override void Do(DictManager manager, TextWriter logger)
    {
        var tmp = Path.GetTempFileName();
        using var sw = new StreamWriter(File.Create(tmp));
        using var fs = File.OpenRead(manager.DictionaryFullPath);
        using var sr = new StreamReader(fs);
        var reForCleaning = new Regex("<link rel=\"stylesheet\" type=\"text/css\" href=\".*?\">|<img.*?>|<a href=\"sound://.*?\"> ?</a>|<script.*?></script>|<span class=\"xref_to_full_entry\">See <a class=\"Ref\" href=\"bword://.+?\" title=\" definition in  \">full entry</a></span>|<a href=\"/definition/.+?\">", RegexOptions.Compiled);
        while (sr.ReadLine() is { } line)
        {
            var split = line.Split('\t');
            var dt = split[0];

            // kindle 不支持这么长的词组查询
            // 但是作为词典应当提供学习的功能，故不删除
            // if (Utils.GetSubStringIndexes(dt, " ", 0).Length >= 4)
            // {
                // continue;
            // }
            
            var dd = split[1];
            
            var newLine = reForCleaning.Replace(line, "")
                .Replace("something/somebody", "sth./sb.")
                .Replace("somebody/something", "sb./sth.")
                // 莫名其妙的缩进，影响阅读
                .Replace("<O10></O10>", "")
                // 没用的东西，软件无法处理
                .Replace(
                    "<a class=\"responsive_display_inline_on_smartphone link-right\" href=\"#relatedentries\">jump to other results</a>",
                    "");
            // 下面进行词性分隔
            // 当 @"id=""entryContent""" 存在多次时，才进行词行分隔
            if (newLine.IndexOf(@"id=""entryContent""", StringComparison.Ordinal) !=
                newLine.LastIndexOf(@"id=""entryContent""", StringComparison.Ordinal))
            {
                const string pattern = @"<div id=""entryContent"" class=""";
                var indexes = Utils.GetSubStringIndexes(dd, pattern, 0);
                var linkCss = dd[..indexes[0]];
                var builder = new StringBuilder();
                for (var i = 0; i < indexes.Length; i++)
                {
                    builder.Append($"{dt}\t{linkCss}");
                    if (i < indexes.Length - 1)
                    {
                        builder.Append(dd[indexes[i]..indexes[i + 1]]);
                        builder.Append('\n');
                    }
                    else
                    {
                        // 最后一个词性，不用换行，因为我们使用的是 sw.WriteLine
                        builder.Append(dd[indexes[i]..]);
                    }
                }

                newLine = builder.ToString();
            }

            sw.WriteLine(newLine);
        }

        sw.Flush();
        sw.Close();
        fs.Close();
        File.Delete(manager.DictionaryFullPath);
        File.Move(tmp, manager.DictionaryFullPath);
    }

    public override Task DoAsync(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第零步不支持 Async");
    }
}