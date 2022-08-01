#if DEBUG
#define SKIP_STEP_ZERO
#endif

using System.Text;
using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepZero : AbsStep
{
    public override string Description => "清除图片、音乐、JS、无效链接等 Kindle 上无法正常使用的功能";
    public override bool EnableAsync => false;

    public override void Do(DictManager manager, TextWriter logger)
    {
#if SKIP_STEP_ZERO
#else
        var tmp = Path.GetTempFileName();
        using var sw = new StreamWriter(File.Create(tmp));
        using var fs = File.OpenRead(manager.DictionaryFullPath);
        using var sr = new StreamReader(fs);
        while (sr.ReadLine() is { } line)
        {
            var newLine = Regex
                // 分别对应 图片 音乐 JS
                .Replace(line, "(<img.*?>|<a href=\"sound://.*?\"> ?</a>|<script.*?></script>)", _ => "")
                .Replace("something/somebody", "sth./sb.")
                .Replace("somebody/something", "sb./sth.")
                // 莫名其妙的缩进，影响阅读
                .Replace("<O10></O10>", "")
                // 没用的东西，软件无法处理
                .Replace(
                    "<a class=\"responsive_display_inline_on_smartphone link-right\" href=\"#relatedentries\">jump to other results</a>",
                    "");
            // // 部分词典会自带添加标题，而我们在处理时会自动添加标题，使得标题重复
            // newLine = Regex.Replace(newLine, "<h1.*?</h1>", _ => "");
            // 没用的东西
            newLine =
                Regex.Replace(newLine,
                    "<span class=\"xref_to_full_entry\">See <a class=\"Ref\" href=\"bword://.+?\" title=\" definition in  \">full entry</a></span>",
                    _ => "");
            // 程序无法解析
            newLine =
                Regex.Replace(newLine,
                    "<a href=\"/definition/.+?\">",
                    _ => "<a>");

            // 下面进行词性分隔
            // 当 @"id=""entryContent""" 存在多次时，才进行词行分隔
            if (newLine.IndexOf(@"id=""entryContent""", StringComparison.Ordinal) !=
                newLine.LastIndexOf(@"id=""entryContent""", StringComparison.Ordinal))
            {
                var split = newLine.Split('\t');
                var dt = split[0];
                var dd = split[1];
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
#endif
    }

    public override Task DoAsync(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第零步不支持 Async");
    }
}