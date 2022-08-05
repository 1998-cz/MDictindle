using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using CommandLine;
using CommandLine.Text;
using MDictindle.Step;

namespace MDictindle
{
    public static class Program
    {
        [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
        [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
        [SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
        private class Options
        {
            [Option("clean-dict", Required = false, Default = false,
                HelpText = "在执行读取与转换之前先清理词典（即第零步），去除 Kindle 上无用功能。\n警告：在执行第零步时，会修改源词典，请您自行做好备份！\n")]
            public bool CleanDictBeforeAct { get; set; }

            [Option("css", Required = false, HelpText = "词典所使用的 CSS 路径")]
            public string? CssPath { get; set; }

            [Value(0, MetaName = "PathToDictionary", HelpText = "待处理的词典源文件。", Required = true)]
            public string Path2Dictionary { get; set; } = "";
        }

        /// <summary>
        /// 检查输入是否有问题，如果有，**终止程序**
        /// </summary>
        [SuppressMessage("ReSharper", "InvertIf")]
        private static void EnvironmentCheck(ref Options opt)
        {
            var path2ProcessFile = opt.Path2Dictionary;
            var css = opt.CssPath;

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

            if (css is not null)
            {
                match = Regex.Match(css, pattern);
                if (match.Success)
                {
                    css = match.Groups[1].Value;
                }

                if (!File.Exists(css))
                {
                    Console.WriteLine($"致命：CSS 文件(\"{css}\")不存在");
                    Environment.Exit((int)ExitCodes.FileNotExist);
                }
            }

            opt.Path2Dictionary = path2ProcessFile;
            opt.CssPath = css;
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

        private const string VersionName = "0.2.0";

        private static async Task RunOptions(Options opt)
        {
            Console.Write($"MDictindle v{VersionName} : 转换 Mdx 到 Mobi 的桥梁\n" +
                          "本软件能够就较短的时间内简化词典，同时把已经转化成 .txt 的 .mdx 词典转换成 .opf 及附属文件，并且给记录了变形词的词典附上变形词支持、补全链接跳转、给单词的不同词性创建单独意项、添加 CSS\n" +
                          "警告：在执行第零步（词典化简）时，会修改源词典，请您自行做好备份！\n" +
                          "作者：子恒\n本软件在 GPLv3.0 许可协议下开放源代码于 https://github.com/TsihenHo/MDictindle\n\n");

            var cleanDict = opt.CleanDictBeforeAct;
            var path2ProcessFile = opt.Path2Dictionary;

            EnvironmentCheck(ref opt);

            var mgr = new DictManager(path2ProcessFile, opt.CssPath);
            Console.WriteLine("程序将依次执行第零步至第三步。");
            Console.WriteLine(
                "部分参数及输出文件如下：\n" +
                $"  源文件：\t{path2ProcessFile}\n" +
                $"  执行第零步：\t{(cleanDict ? '是' : '否')}\n" +
                $"  CSS 路径：\t{opt.CssPath}\n" +
                $"  输出的OPF：\t{Path.Combine(mgr.DictionaryDirPath, mgr.DictionaryName + ".opf")}"
            );

            var timer = new Stopwatch();
            timer.Start();
#if DEBUG
#else
            try
            {
#endif
            if (cleanDict)
            {
                var step0 = new AbsStepZero();
                Console.WriteLine($"第零步开始：{step0.Description}");
                await step0.AutoDo(mgr, Console.Out);
                Console.WriteLine("第零步完成！");
            }
            else
            {
                Console.WriteLine("提示：您跳过了第零步，请确保您的词典已经被第零步处理过，否则可能会出现莫名其妙的问题。");
                Console.WriteLine("跳过第零步。");
            }

            var step1 = new AbsStepOne();
            var step2 = new AbsStepTwo();
            var step3 = new AbsStepThree();
            Console.WriteLine($"第一步开始：{step1.Description}");
            await step1.AutoDo(mgr, Console.Out);
            Console.WriteLine("第一步完成！");
            Console.WriteLine($"第二步开始：{step2.Description}");
            await step2.AutoDo(mgr, Console.Out);
            Console.WriteLine("第二步完成！");
            Console.WriteLine($"第三步开始：{step3.Description}");
            // ReSharper disable once MethodHasAsyncOverload
            step3.Do(mgr, Console.Out);
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
            mgr.Dispose();
            timer.Stop();
            Console.WriteLine($"成功！耗时 {timer.ElapsedMilliseconds / 1000.0} 秒。\n" +
                              $"现在，您可以编辑 {Path.Combine(mgr.DictionaryDirPath, mgr.DictionaryName + ".opf")} 中的内容，之后使用 mobigen 或 prcgen 生成词典！\n" +
                              "按下任意键继续...");
            Console.ReadKey();
            Environment.Exit((int)ExitCodes.Ok);
        }

        private static void RunErrors(ParserResult<Options> res)
        {
            var text = HelpText.AutoBuild(res);
            Console.WriteLine(text);

            Environment.Exit((int)ExitCodes.UnknownArgs);
        }

        private static async Task Main(string[] args)
        {
            var res = Parser.Default.ParseArguments<Options>(args);
            if (res is Parsed<Options> parsed)
            {
                await RunOptions(parsed.Value);
            }
            else
            {
                RunErrors((NotParsed<Options>)res);
            }
        }
    }
}