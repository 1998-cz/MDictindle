// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using MDictindle.Step;


namespace MDictindle
{
    public static class Program
    {
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

            var mgr = new DictManager(path2ProcessFile!);
            Console.WriteLine("程序将依次执行第零步至第三步。");
            Console.WriteLine(
                "部分文件路径如下：\n" +
                $"\t源文件：\t\t{path2ProcessFile}\n" +
                $"\t输出的OPF：\t\t{Path.Combine(mgr.DictionaryDirPath, mgr.DictionaryName + ".opf")}"
            );

            var timer = new Stopwatch();
            timer.Start();

#if DEBUG
            // 调试时使用调试器的异常捕获
#else
            try
            {
#endif
            var step0 = new AbsStepZero();
            var step1 = new AbsStepOne();
            var step2 = new AbsStepTwo();
            var step3 = new AbsStepThree();
            Console.WriteLine($"第零步开始：{step0.Description}");
            await step0.AutoDo(mgr, Console.Out);
            Console.WriteLine("第零步完成！");
            Console.WriteLine($"第一步开始：{step1.Description}");
            await step1.AutoDo(mgr, Console.Out);
            Console.WriteLine("第一步完成！");
            Console.WriteLine($"第二步开始：{step2.Description}");
            await step2.AutoDo(mgr, Console.Out);
            Console.WriteLine("第二步完成！");
            Console.WriteLine($"第三步开始：{step3.Description}");
            // ReSharper disable once MethodHasAsyncOverload
            step3.Do(mgr, Console.Out);
            Console.WriteLine("第三步开始！");
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
                              $"现在，您可以编辑 {Path.Combine(mgr.DictionaryDirPath, mgr.DictionaryName + ".opf")} 中的内容，之后使用 mobigen 或 prcgen 生成词典！\n" +
                              $"按下任意键继续...");
            Console.ReadKey();
            Environment.Exit((int)ExitCodes.Ok);
        }
    }
}