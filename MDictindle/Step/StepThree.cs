using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepThree : AbsStep
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
<idx:orth value=""{0}"">{4}</idx:orth><idx:key key=""{1}"">
{2}
</idx:entry>
<mbp:pagebreak/>
";

    public override string Description => "写出到文件";
    public override bool EnableAsync => false;

    public override void Do(DictManager manager, TextWriter logger)
    {
        StreamWriter? opf = null;
        
        foreach (var entry in manager.Entries)
        {
            var index = entry.Index;
            if (index % 2000 == 0)
            {
                opf?.Write(@"</mbp:frameset></body></html>");
                // 太快了文件写不出来
                Thread.Sleep(30);
                opf?.Close();
                GC.Collect();
                logger.WriteLine($"第三步：正在处理第{index / 2000}号文件");
                opf = new StreamWriter(Path.Combine(manager.DictionaryDirPath,
                    $"{manager.DictionaryName}{index / 2000}.html"));
                opf.AutoFlush = true;
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

            opf!.Write(OpfEntryWithID, entry.Name, entry.Key, File.ReadAllText(entry.ExplanationFilePath), entry.Id, entry.GetInflString());
            opf.Flush();
        }
        var entriesNum = manager.EntryNumber - 1;

        opf!.Write(@"</mbp:frameset></body></html>");
        opf.Close();

        opf = new StreamWriter(File.Create(Path.Combine(manager.DictionaryDirPath, $"{manager.DictionaryName}.opf")));
        opf.AutoFlush = true;

        var ts = DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        opf.Write(OpfHead1, Convert.ToInt64(ts.TotalSeconds), manager.DictionaryName);
        opf.Write(OpfHead2);
        for (var j = 0; j <= entriesNum / 2000; j++)
        {
            opf.Write(OpfLine, j, manager.DictionaryName);
        }

        opf.Write(OpfMiddle);
        for (var j = 0; j <= entriesNum / 2000; j++)
        {
            opf.Write(OpfItemRef, j);
        }

        opf.Write(OpfEnd);
        opf.Flush();
        Thread.Sleep(30);
        opf.Close();
    }

    public override Task DoAsync(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第三步不支持 Async");
    }
}