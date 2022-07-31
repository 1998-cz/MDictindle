// See https://aka.ms/new-console-template for more information

#if DEBUG
// DEV ONLY
#define NO_ZERO
#endif

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;


namespace MDictindle
{
    public static class Program
    {
        private const string OpfHead1 = @"<?xml version=""1.0""?><!DOCTYPE package SYSTEM ""oeb1.ent"">
<package unique-identifier=""uid"" xmlns:dc=""Dublin Core"">
 
<metadata>
<dc-metadata>
<dc:Identifier id=""uid"">{0}</dc:Identifier>
<dc:Title><h2>{1}</h2></dc:Title>
<dc:Language>EN</dc:Language>
</dc-metadata>
<x-metadata>
";

        private const string OpfHead2 = @"
<DictionaryInLanguage>en-us</DictionaryInLanguage>
<DictionaryOutLanguage>en-us</DictionaryOutLanguage>
</x-metadata>
</metadata>
<manifest>
";

        private const string OpfLine =
            "<item id=\"dictionary{0}\" href=\"{1}{0}.html\" media-type=\"text/x-oeb1-document\"/>\n";

        private const string OpfMiddle = "</manifest><spine>\n";

        private const string OpfItemRef = "<itemref idref=\"dictionary{0}\"/>\n";

        private const string OpfEnd = @"</spine>
<tours/>
<guide> <reference type=""search"" title=""Dictionary Search"" onclick= ""index_search()""/> </guide>
</package>
";

        // ReSharper disable once InconsistentNaming
        private const string OpfEntryWithID = @"<idx:entry name=""word"" scriptable=""yes"" id=""{3}"">
<idx:orth value=""{0}""></idx:orth><idx:key key=""{1}"">
{2}
</idx:entry>
<mbp:pagebreak/>
";

        private static string FileNameWithoutExtension { get; set; } = "";
        private static string FilePath { get; set; } = "";
        private static string FileDirPath => Path.GetDirectoryName(FilePath)!;

        /// <summary>
        /// 检查输入是否有问题，如果有，**终止程序**
        /// </summary>
        /// <param name="path2ProcessFile"></param>
        [SuppressMessage("ReSharper", "InvertIf")]
        private static void EnvironmentCheck(ref string? path2ProcessFile)
        {
            // 输入是否 null
            if (path2ProcessFile is null)
            {
                Console.WriteLine("致命：文件路径输入失败");
                Environment.Exit((int)ExitCodes.ReadFilePathFailed);
            }

            const string pattern = @"""*'*(.+(?=['""]))""*'*";
            var match = Regex.Match(path2ProcessFile, pattern);
            if (match.Success)
            {
                path2ProcessFile = match.Groups[1].Value;
            }

            // 词典源文件是否存在
            if (!File.Exists(path2ProcessFile))
            {
                Console.WriteLine($"致命：待处理的文件(\"{path2ProcessFile}\")不存在");
                Environment.Exit((int)ExitCodes.FileNotExist);
            }

            if (Path.GetFileName(path2ProcessFile).Contains('@'))
            {
                Console.WriteLine("致命：为防止潜在问题，请不要使用 '@' 作为文件名");
                Environment.Exit((int)ExitCodes.FileNameWithAt);
            }
        }

        // ReSharper disable once InconsistentNaming
        /// <summary>
        /// 单词->ID，在处理链接时有用
        /// </summary>
        private static IDictionary<string, string> IDMappings { get; } = new Dictionary<string, string>();

        /// <summary>
        /// 读取词典中的 Links
        /// </summary>
        /// <param name="process"></param>
        /// <exception cref="PathTooLongException"></exception>
        /// <exception cref="UnauthorizedAccessException"></exception>
        /// <exception cref="IOException"></exception>
        private static async Task<Dictionary<string, ISet<string>>> ReadDictionaryMappingsAsync(
            TextReader process)
        {
            var mappings = new Dictionary<string, ISet<string>>();

            // 读取每一行到 l 中
            while (await process.ReadLineAsync() is { } l)
            {
                // 过滤掉牛津 3000 词和牛津词典里面杂七杂八的不是单词的东西，他们只是HTML装饰，不需要参与单词 link 分析
                if (l.StartsWith("@opal_spoken") || l.StartsWith("@opal_written") || l.StartsWith("ox3000")
                    || l.StartsWith("ox5000") || l.StartsWith("@topic_"))
                {
                    GC.Collect();
                    continue;
                }

                // 这个长度不正常，不可能是 @@@LINK 类型，如果上正则会耽误很多时间，保险起见，看看长度
                if (l.Length >= 300 && !l.Contains("@@@LINK="))
                {
                    continue;
                }

                // 使用正则匹配诸如 xxx @@@LINK=yyy 的行
                var matches = Regex.Match(l, @"([^\t]*?)\t@@@LINK=(.*?)\\?n?$");
                // 如果没有 @@@LINK，则表明这是正文，没有对应关系
                if (!matches.Success)
                {
                    // await save.WriteAsync(l);
                    // _ = l.Split('\t')[0];
                    // await logger.WriteLineAsync($"第一步：略过单词/词组：{word}");
                    continue;
                }

                // example: going @@@LINK=go
                // going 是 sourceWord，go 是 linkWord
                var sourceWord = matches.Groups[1].Value;
                var linkWord = matches.Groups[2].Value;

                if (!mappings.ContainsKey(linkWord))
                {
                    mappings[linkWord] = new HashSet<string>();
                }

                mappings[linkWord].Add(sourceWord);

                // await logger.WriteLineAsync($"第一步：写入对应关系： {linkWord} -> {sourceWord}");
            }

            return mappings;
        }


        /// <summary>
        /// Tab2Opf 的功能，翻译自 tab2opf.py 并做了一些修改，下面是 License
        /// 
        /// <para>
        /// Copyright (C) 2007 - Klokan Petr Přidal (www.klokan.cz)
        ///
        /// This library is free software; you can redistribute it and/or
        /// modify it under the terms of the GNU Library General Public
        /// License as published by the Free Software Foundation; either
        /// version 2 of the License, or (at your option) any later version.
        ///
        /// This library is distributed in the hope that it will be useful,
        /// but WITHOUT ANY WARRANTY; without even the implied warranty of
        /// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
        /// Library General Public License for more details.
        ///
        /// You should have received a copy of the GNU Library General Public
        /// License along with this library; if not, write to the
        /// Free Software Foundation, Inc., 59 Temple Place - Suite 330,
        /// Boston, MA 02111-1307, USA.
        /// </para>
        /// </summary>
        /// <param name="process"></param>
        /// <param name="logger"></param>
        private static void Tab2Opf(TextReader process, TextWriter? logger = null)
        {
            logger ??= Console.Out;
            // i 表示行数，0 开始
            var i = 0;
            TextWriter? opf = null;
            // 读取每一行到 l 中
            while (process.ReadLine() is { } l)
            {
                if (i % 10_000 == 0)
                {
                    opf?.WriteAsync(@"</mbp:frameset></body></html>");
                    opf?.Close();
                    logger.WriteLine($"第二步：正在处理第{i / 10_000}号文件");
                    opf = new StreamWriter(File.Create(Path.Combine(FileDirPath,
                        $"{FileNameWithoutExtension}{i / 10_000}.html.ori")));
                    opf.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<html xmlns:idx=""www.mobipocket.com"" xmlns:mbp=""www.mobipocket.com"" xmlns:xlink=""http://www.w3.org/1999/xlink"">
<body>
<mbp:pagebreak/>
<mbp:frameset>
<mbp:slave-frame display=""bottom"" device=""all"" breadth=""auto"" leftmargin=""0"" rightmargin=""0"" bottommargin=""0"" topmargin=""0"">
<div align=""center"" bgcolor=""yellow""/>
<a onclick=""index_search()"">Dictionary Search</a>
</div>
</mbp:slave-frame>
<mbp:pagebreak/>
");
                }

                if (Regex.IsMatch(l, @"^\s*$"))
                {
                    continue;
                }

                var array = l.Split('\t');
                var dt = array[0];
                var dd = array[1].Replace("\\\\", "\\").Replace("\\n", "<br/>\n");

                if (dd.StartsWith("@@@LINK=", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                // 这个字符串后面就是单词的 ID 如：
                // <div class="entry" sk="frenchbraid: :10" hlength="12" hclass="entry" sum="227" htag="section" id="french-braid" idm_id="000023406">
                const string pattern =
                    "<div class=\"entry\"";
                var index = dd.IndexOf(pattern, StringComparison.Ordinal);
                var id = dt;
                if (index != -1)
                {
                    var start = dd.IndexOf("id=\"", index + pattern.Length, StringComparison.Ordinal);
                    // 4 == "id=\"".Length
                    var end = dd.IndexOf('"', start + 4);
                    id = dd[(start + 4)..end];
                }

                // 去除特殊字符
                id = id.Replace("@", "at_")
                    .Replace('=', '_')
                    .Replace('&', '_')
                    .Replace(' ', '_');
                    // .Replace('-', '_');

                var dtstrip = dt.Normalize();

                opf!.Write(OpfEntryWithID, dt, dtstrip, dd, id);
                IDMappings[dt] = id;
                opf.Flush();

                i++;
            }

            // entriesNum 不需要统计真正的词组数，他是用来计算文件数的工具
            var entriesNum = i - 1;

            opf!.Write(@"</mbp:frameset></body></html>");
            opf.Close();

            opf = new StreamWriter(File.Create(Path.Combine(FileDirPath, $"{FileNameWithoutExtension}.opf")));

            var ts = DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0);
            opf.Write(OpfHead1, Convert.ToInt64(ts.TotalSeconds), FileNameWithoutExtension);
            opf.Write(OpfHead2);
            for (var j = 0; j <= entriesNum / 10_000; j++)
            {
                opf.Write(OpfLine, j, FileNameWithoutExtension);
            }

            opf.Write(OpfMiddle);
            for (var j = 0; j <= entriesNum / 10_000; j++)
            {
                opf.Write(OpfItemRef, j);
            }

            opf.Write(OpfEnd);
            opf.Flush();
        }

        /// <summary>
        /// 生成变形词跳转数据
        /// </summary>
        /// <param name="words"></param>
        /// <returns></returns>
        private static string GenerateInfl(IEnumerable<string> words)
        {
            const string front = "<idx:infl>";
            const string end = "</idx:infl>";
            var sb = new StringBuilder(front);
            foreach (var word in words)
            {
                sb.Append($"<idx:iform name=\"\" value=\"{word}\" />");
            }

            sb.Append(end);
            return sb.ToString();
        }

        private static IEnumerable<List<T>> GetPermutationAndCombination<T>(ISet<T>[] sets)
        {
            switch (sets.Length)
            {
                case 1:
                    return sets[0].Select(it => new List<T> { it }).ToHashSet();
                case 0:
                    throw new ArgumentException("没有参数", nameof(sets));
            }

            var res = new HashSet<List<T>>();
            // sets 长度至少是 2
            foreach (var t in sets[0])
            {
                var res2 = GetPermutationAndCombination(sets[1..]);
                foreach (var list in res2)
                {
                    list.Insert(0, t);
                    res.Add(list);
                }
            }

            return res;
        }

        private static string CreateInflsForAWordOrPhrase(string wordOrPhrase, IReadOnlyDictionary<string, ISet<string>> mappings, TextWriter logger)
        {
            // 查看 是不是 短语
            var words = wordOrPhrase.Split(' ');
            if (words.Length == 1)
            {
                if (!mappings.ContainsKey(wordOrPhrase)) return "";
                var res = GenerateInfl(mappings[wordOrPhrase]);
                return res;

            }
            // 短语

            {
                var listInfls = new List<ISet<string>>();
                foreach (var singleWord in words)
                {
                    var set = mappings.ContainsKey(singleWord) ? mappings[singleWord] : new HashSet<string>();
                    set.Add(singleWord);
                    listInfls.Add(set);
                }

                var arr = listInfls.ToArray();
                var res = GetPermutationAndCombination(arr);
                var finalPhrases = res.Select(list => string.Join(' ', list)).ToList();
                if (finalPhrases.Count > 254)
                {
                    // todo 创建一模一样的词条来容纳更多的变形
                    logger.WriteLine($"第三步：警告：词组 {wordOrPhrase} 的变形数量达到 {finalPhrases.Count} 种，由于 Kindle 只支持 255 种变形，所以只保留 254 种");
                    finalPhrases.RemoveRange(254, finalPhrases.Count - 254);
                }
                return GenerateInfl(finalPhrases);
            }
        }

        /// <summary>
        /// 把变形词写入磁盘
        /// </summary>
        /// <param name="mappings">变形词对应关系</param>
        /// <param name="logger"></param>
        /// <returns>各个单词的文件数</returns>
        private static Dictionary<string, int> WriteInflsOut(
            IReadOnlyDictionary<string, ISet<string>> mappings,
            TextWriter? logger = null
        )
        {
            logger ??= Console.Out;

            var files = Directory.GetFiles(FileDirPath)
                .Where(it => Regex.IsMatch(it, @$"{FileNameWithoutExtension}\d+\.html\.ori"))
                .ToArray();

            var wordLocations = new Dictionary<string, int>();
            foreach (var file in files)
            {
                logger.WriteLine($"第三步：处理文件 {Path.GetFileName(file)}");
                using var f = File.Open(file, FileMode.Open);
                using var reader = new StreamReader(f);
                using var g = File.Create(file[..^4]);
                using var writer = new StreamWriter(g);
                writer.AutoFlush = true;
                while (reader.ReadLine() is { } l)
                {
                    const string pattern = @"<idx:orth value=""([^<]*)"">";
                    var match = Regex.Match(l, pattern);
                    if (!match.Success)
                    {
                        writer.WriteLine(l);
                        continue;
                    }

                    var word = match.Groups[1].Value;
                    // 注意：后缀名实际上是 .html.ori，所以是 ^9
                    wordLocations[word] =
                        Convert.ToInt32(Path.GetFileName(file[..^9]).Replace(FileNameWithoutExtension, ""));
                    // if (mappings.ContainsKey(word))
                    // {
                    //     var infls = GenerateInfl(mappings[word]);
                    //     writer.WriteLine(match.Value + infls + "</idx:orth>");
                    // }
                    // else
                    // {
                    //     writer.WriteLine(l);
                    //     // continue;
                    // }
                    writer.WriteLine(match.Value + CreateInflsForAWordOrPhrase(word, mappings, logger) + "</idx:orth>");
                }

                reader.Close();
                f.Close();
                File.Delete(file);
            }

            // // 增加变形词的支持
            // // 如：
            // // 最开始里面只会记录 Chicago
            // // 但经过处理过后这就会记录 Chicago 的所有变形词
            // foreach (var pair in mappings)
            // {
            //     foreach (var s in pair.Value)
            //     {
            //         if (wordLocations.ContainsKey(pair.Key))
            //         {
            //             wordLocations[s] = wordLocations[pair.Key];
            //         }
            //     }
            // }

            return wordLocations;
        }

        /// <summary>
        /// 处理词典内部的链接
        /// </summary>
        /// <param name="locations">各个单词所在的页码</param>
        /// <param name="mappings">单词->变形词</param>
        /// <param name="logger"></param>
        private static void ProcessLinks(
            IReadOnlyDictionary<string, int> locations,
            IReadOnlyDictionary<string, ISet<string>> mappings,
            TextWriter? logger = null
        )
        {
            logger ??= Console.Out;

            var reverseMappings = new Dictionary<string, string>();
            foreach (var pair in mappings)
            {
                foreach (var s in pair.Value)
                {
                    reverseMappings[s] = pair.Key;
                }

                reverseMappings[pair.Key] = pair.Key;
            }

            var files = Directory.GetFiles(FileDirPath)
                .Where(it => Regex.IsMatch(it, @$"{FileNameWithoutExtension}\d+\.html"))
                .ToArray();
            foreach (var file in files)
            {
                /*
                 * 注意：这个函数逻辑比较乱，我简单说一下，0.1.2 版本应该会对这个函数作出改动
                 * 
                 */
                logger.WriteLine($"第四步：处理文件 {Path.GetFileName(file)}");
                var tmp = Path.GetTempFileName();
                using var sw = new StreamWriter(File.Create(tmp));
                using var fs = File.OpenRead(file);
                using var sr = new StreamReader(fs);
                while (sr.ReadLine() is { } line)
                {
                    var newLine = new StringBuilder(line);
                    // 存在引用
                    if (line.Contains("a class=\"Ref\""))
                    {
                        const string pattern =
                            "<a class=\"Ref\" href=\"(bword://(\\S+))\" title=\".+?\">";
                        var collection = Regex.Matches(line, pattern);
                        foreach (Match match in collection)
                        {
                            var word = match.Groups[2].Value;
                            // 1. 没有 # 的情况
                            if (!word.Contains('#'))
                            {
                                // 例：word="chicago" actualWord="Chicago"
                                if (!reverseMappings.TryGetValue(word, out var actualWord))
                                {
                                    actualWord = word;
                                }

                                // 1.1 若不存在这个词语，则跳转到下面发警告
                                if (locations.ContainsKey(actualWord))
                                {
                                    var location = locations[actualWord];
                                    if (!IDMappings.TryGetValue(actualWord, out var id))
                                    {
                                        id = actualWord;
                                    }

                                    newLine.Replace(match.Value,
                                        // // 应当使用 word 而不是 actualWord
                                        $"<a href=\"{FileNameWithoutExtension}{location}.html#{id}\">");
                                    continue;
                                }
                            }
                            // 2. 包含 # 的情况
                            else
                            {
                                var split = word.Split('#');
                                var prefix = split[0];
                                var postfix = split[1];
                                // 例：word="attachment#attachment_sng_6"
                                // prefix="attachment"
                                // actualPrefix="attachment"
                                if (!reverseMappings.TryGetValue(prefix, out var actualPrefix))
                                {
                                    actualPrefix = prefix;
                                }

                                // 2.1 若不存在这个前缀，则跳转到下面发警告
                                if (locations.ContainsKey(actualPrefix))
                                {
                                    var location = locations[actualPrefix];
                                    // 经验之谈
                                    string? id;
                                    if (!postfix.EndsWith("_e") && GetSubStringIndexes(postfix, "_", 0).Length <= 1)
                                    {
                                        if (!IDMappings.TryGetValue(postfix, out id))
                                        {
                                            logger.WriteLine($"第四步：警告：没有找到 {postfix} 对应的 id");
                                            id = postfix;
                                        }
                                    }
                                    else
                                    {
                                        if (!IDMappings.TryGetValue(actualPrefix, out id))
                                        {
                                            if (!IDMappings.TryGetValue(prefix, out id))
                                            {
                                                logger.WriteLine($"第四步：警告：没有找到 {actualPrefix} 对应的 id");
                                                id = actualPrefix;
                                            }
                                        }
                                    }

                                    var link = $"{FileNameWithoutExtension}{location}.html#{id}";
                                    newLine.Replace(match.Value,
                                        $"<a href=\"{link}\">");
                                    continue;
                                }
                            }

                            newLine.Replace(match.Value, "<a>");
                            logger.WriteLine($"第四步：警告：有指向 {word} 的链接，但 {word} 本身不存在");
                            // 自动continue;
                        }
                    }

                    if (line.Contains("<a href=\"bword://"))
                    {
                        const string pattern =
                            "<a href=\"(bword://(\\S+))\">";
                        var collection = Regex.Matches(line, pattern);
                        // 经检查，不会有 #

                        foreach (Match match in collection)
                        {
                            var word = match.Groups[2].Value;
                            if (!reverseMappings.TryGetValue(word, out var actualWord))
                            {
                                actualWord = word;
                            }

                            if (locations.ContainsKey(actualWord))
                            {
                                var location = locations[actualWord];
                                if (!IDMappings.TryGetValue(actualWord, out var id))
                                {
                                    id = actualWord;
                                }

                                newLine.Replace(match.Value,
                                    $"<a href=\"{FileNameWithoutExtension}{location}.html#{id}\">");
                                continue;
                            }

                            newLine.Replace(match.Value, "<a>");
                            logger.WriteLine($"第四步：警告：有指向 {word} 的链接，但 {word} 本身不存在");
                        }
                    }

                    // 针对 ox3000 word/ox5000 word/@opal_ ...
                    if (line.Contains("<a href=\"bword://@"))
                    {
                        const string pattern =
                            // 之所以要用 @\S+? 而不是 \S+? ，是因为后者会匹配 dic0.html#@bala 而前者不会 
                            @"<a href=""bword://(@\S+?)"">";
                        var collection = Regex.Matches(line, pattern);
                        foreach (Match match in collection)
                        {
                            var word = match.Groups[1].Value;
                            // 这玩意没有变形
                            // if (!reverseMappings.TryGetValue(word, out var actualWord))
                            // {
                            //     actualWord = word;
                            // }

                            if (!locations.TryGetValue(word, out var location))
                            {
                                // 没有哦，不管吧
                                newLine.Replace(match.Value, "<a>");
                                logger.WriteLine($"第四步：警告：有指向 {word} 的链接，但 {word} 本身不存在");
                                continue;
                            }

                            var id = word.Replace("@", "at_")
                                .Replace('=', '_')
                                .Replace('&', '_')
                                .Replace(' ', '_');
                                // .Replace('-', '_');
                            // 在这里 id==word 所以不用添加判断
                            newLine.Replace(match.Value,
                                $"<a href=\"{FileNameWithoutExtension}{location}.html#{id}\">");
                            // newLine.Replace(match.Groups[1].Value, $"{FileNameWithoutExtension}{location}.html#{word}");
                        }
                    }

                    sw.WriteLine(newLine);
                }

                sw.Flush();
                sw.Close();
                fs.Close();
                File.Delete(file);
                File.Move(tmp, file);
            }
        }

        private static int[] GetSubStringIndexes(string str, string substr, int startPos)
        {
            int foundPos;
            var foundItems = new List<int>();
            do
            {
                foundPos = str.IndexOf(substr, startPos, StringComparison.Ordinal);
                if (foundPos == -1) continue;
                startPos = foundPos + substr.Length;
                foundItems.Add(foundPos);
            } while (foundPos > -1 && startPos < str.Length);

            return foundItems.ToArray();
        }

#if NO_ZERO
#else
        private static void SimplifyDictionary()
        {
            var tmp = Path.GetTempFileName();
            using var sw = new StreamWriter(File.Create(tmp));
            using var fs = File.OpenRead(FilePath);
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
                    var indexes = GetSubStringIndexes(dd, pattern, 0);
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
            File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
#endif

#if DEBUG
#else
        private static void FatalError(Exception e, string desc, ExitCodes code)
        {
            Console.WriteLine(
                $"\n\n致命：{desc}\n" +
                "==========详细信息==========\n" +
                e
            );
            Environment.Exit((int)code);
        }
#endif

        private const string VersionName = "0.1.1";

        public static async Task Main()
        {
            Console.Write($"MDictindle v{VersionName} : 转换 Mdx 到 Mobi/Prc 的帮手\n" +
                          "本软件能够就较短的时间内简化词典，同时把已经转化成 .txt 的 .mdx 词典转换成 .opf 及附属文件，并且给记录了变形词的词典附上变形词支持、补全链接跳转、给单词的不同词性创建单独意项\n" +
                          "警告：在执行第零步（词典化简）时，会修改源词典，请您自行做好备份！\n" +
                          "作者：子恒\n本软件在 GPLv3.0 许可协议下开放源代码于 https://github.com/TsihenHo/MDictindle\n\n" +
                          "请输入词典源文件(.txt)路径：");

            var path2ProcessFile = Console.ReadLine();

            EnvironmentCheck(ref path2ProcessFile);

            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(path2ProcessFile)!;
            FilePath = path2ProcessFile!;

            Console.WriteLine("程序将依次执行第零步至第四步。其中，第一步和第二步同时执行，都执行完毕后执行下一步。");
            Console.WriteLine(
                "部分文件路径如下：\n" +
                $"\t源文件：\t\t{path2ProcessFile}\n" +
                $"\t输出的OPF：\t\t{Path.Combine(FileDirPath, FileNameWithoutExtension + ".opf")}"
            );

            var timer = new Stopwatch();
            timer.Start();

#if DEBUG
            // 调试时使用调试器的异常捕获
#else
            try
            {
#endif

# if NO_ZERO
// 调试时跳过这个部分
#else
# if DEBUG
#endif
                // CSS其实能够使用
                Console.WriteLine("第零步开始：清除图片、音乐、JS、无效链接等 Kindle 上无法正常使用的功能");
                SimplifyDictionary();
                Console.WriteLine("第零步完成！");
#endif
            await using var f = File.OpenRead(path2ProcessFile!);
            using var process = new StreamReader(f);

            Console.WriteLine("第一步开始：获取词典的变形词对应关系");
            var task1 =
                ReadDictionaryMappingsAsync(process)
                    .ContinueWith((res, _) =>
                    {
                        Console.Out.WriteLine("第一步完成！");
                        return res.Result;
                    }, null);

            Console.WriteLine("第二步开始：执行 Tab2Opf");
            var task2 = Task.Factory.StartNew(() =>
            {
                Tab2Opf(new StreamReader(File.OpenRead(path2ProcessFile!)), Console.Out);
                Console.Out.WriteLine("第二步完成！");
            });

            task1.Wait();
            task2.Wait();

            Console.WriteLine("第三步开始：添加异形词、简化词典");
            var mappings = await task1;
            var locations = WriteInflsOut(mappings);
            Console.WriteLine("第三步完成！");

            Console.WriteLine("第四步开始：处理链接");
            ProcessLinks(locations, mappings, Console.Out);
            Console.WriteLine("第四步完成！");
#if DEBUG
#else
            }
            catch (PathTooLongException e)
            {
                FatalError(e, "文件路径过长", ExitCodes.PathTooLong);
            }
            catch (UnauthorizedAccessException e)
            {
                FatalError(e, "拒绝访问", ExitCodes.UnauthorizedAccess);
            }
            catch (IOException e)
            {
                FatalError(e, "IO 错误", ExitCodes.IO);
            }
            catch (Exception e)
            {
                FatalError(e, "未知错误", ExitCodes.Unknown);
            }

#endif
            timer.Stop();
            Console.WriteLine($"成功！耗时 {timer.ElapsedMilliseconds / 1000.0} 秒。\n" +
                              $"现在，您可以编辑 {Path.Combine(FileDirPath, FileNameWithoutExtension + ".opf")} 中的内容，之后使用 mobigen 或 prcgen 生成词典！\n" +
                              $"按下任意键继续...");
            Console.ReadKey();
            Environment.Exit((int)ExitCodes.Ok);
        }
    }
}