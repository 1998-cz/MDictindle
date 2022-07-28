// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
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
<dc:Title><h2>{0}</h2></dc:Title>
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

        private const string OpfEntry = @"<idx:entry name=""word"" scriptable=""yes"">
<h2>
<idx:orth>{0}</idx:orth><idx:key key=""{1}"">
</h2>
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
        }

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
        /// Tab2Opf 的功能
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
<mbp:pagebreak/>");
                }

                var array = l.Split('\t');
                var dt = array[0];
                var dd = array[1].Replace("\\\\", "\\").Replace("\\n", "<br/>\n");
                if (dd.StartsWith("@@@LINK=", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                var dtstrip = dt.Normalize();

                opf!.Write(OpfEntry, dt, dtstrip, dd);
                opf.Flush();

                i++;
            }

            // entriesNum 不需要统计真正的词组数，他是用来计算文件数的工具
            var entriesNum = i - 1;

            opf!.Write(@"</mbp:frameset></body></html>");
            opf.Close();

            opf = new StreamWriter(File.Create(Path.Combine(FileDirPath, $"{FileNameWithoutExtension}.opf")));

            opf.Write(OpfHead1, FileNameWithoutExtension);
            opf.Write(OpfHead2);
            for (var j = 0; j < entriesNum / 10_000; j++)
            {
                opf.Write(OpfLine, j, FileNameWithoutExtension);
            }

            opf.Write(OpfMiddle);
            for (var j = 0; j < entriesNum / 10_000; j++)
            {
                opf.Write(OpfItemRef, j);
            }

            opf.Write(OpfEnd);
            opf.Flush();
        }

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

        private static void SimplifyDictionary(
            IReadOnlyDictionary<string, ISet<string>> mappings,
            TextWriter? logger = null
        )
        {
            logger ??= Console.Out;

            var files = Directory.GetFiles(FileDirPath)
                .Where(it => Regex.IsMatch(it, @$"{FileNameWithoutExtension}\d+\.html\.ori"))
                .ToArray();

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
                    const string pattern = @"(<idx:orth>([^<]*))";
                    var match = Regex.Match(l, pattern);
                    if (!match.Success)
                    {
                        writer.WriteLine(l);
                        continue;
                    }

                    var word = match.Groups[2].Value;
                    if (mappings.ContainsKey(word))
                    {
                        var infls = GenerateInfl(mappings[word]);
                        writer.WriteLine(match.Groups[1].Value + infls + "</idx:orth>");
                    }
                    else
                    {
                        writer.WriteLine(l);
                        // continue;
                    }
                }
            }
        }

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

        public static async Task Main(string[] args)
        {
            Console.Write("MDictindle : 转换 Mdx 到 Mobi/Prc 的助手\n" +
                          "本软件能够就较短的时间内简化词典，同时把已经转化成 .txt 的 .mdx 词典转换成 .opf 及附属文件，并且给记录了变形词的词典附上变形词支持\n" +
                          "作者：子恒\n" +
                          "请输入词典源文件(.txt)路径：");

            var path2ProcessFile = Console.ReadLine();

            EnvironmentCheck(ref path2ProcessFile);

            FileNameWithoutExtension = Path.GetFileNameWithoutExtension(path2ProcessFile)!;
            FilePath = path2ProcessFile!;

            Console.WriteLine("程序将依次执行第零步、第一步、第二步、第三步。其中，第零步和第三步单独执行，第一步和第二步同时执行，在都执行完毕后执行第三步。");
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
                Console.WriteLine("第零步开始：清除 sound/img 引用（耗时与文件大小相关，对于 300MB 能够在 3 分钟内完成）");
                var tmp = Path.GetTempFileName();
                await using var sw = new StreamWriter(File.Create(tmp));
                await using var fs = File.OpenRead(path2ProcessFile!);
                using var sr = new StreamReader(fs);
                while (await sr.ReadLineAsync() is { } line)
                {
                    var newLine = Regex.Replace(line, "(<img.*?>|<a href=\"sound://.*?\"> ?</a>)", _ => "");
                    await sw.WriteLineAsync(newLine);
                }

                await sw.FlushAsync();
                sw.Close();
                fs.Close();
                File.Delete(path2ProcessFile!);
                File.Move(tmp, path2ProcessFile!);
                timer.Stop();
                Console.WriteLine($"第零步完成！耗时 {timer.ElapsedMilliseconds / 1000.0} 秒。");
                timer.Restart();

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

                Console.WriteLine("第二步开始：执行 tab2opf（这会消耗一些时间）");
                var task2 = Task.Factory.StartNew(() =>
                {
                    Tab2Opf(new StreamReader(File.OpenRead(path2ProcessFile!)), Console.Out);
                    Console.Out.WriteLine("第二步完成！");
                });

                task1.Wait();
                task2.Wait();

                Console.WriteLine("第三步开始：添加异形词、简化词典");
                SimplifyDictionary(await task1);
                Console.WriteLine("第三步完成！");
                
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