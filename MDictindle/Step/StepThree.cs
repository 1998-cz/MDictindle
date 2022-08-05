namespace MDictindle.Step;

public class AbsStepThree : AbsStep
{
    private const string OpfHead1 = @"<?xml version=""1.0""?><!DOCTYPE package SYSTEM ""oeb1.ent"">
<package unique-identifier=""uid"" xmlns:dc=""Dublin Core"">
 
<metadata>
<dc-metadata>
<dc:Identifier id=""uid"">{0}</dc:Identifier>
<dc:Creator>MDctindle</dc:Creator>
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
        "<item id=\"dictionary\" href=\"{0}.html\" media-type=\"text/x-oeb1-document\"/>\n";

    private const string OpfMiddle = "</manifest><spine>\n";

    private const string OpfItemRef = "<itemref idref=\"dictionary\"/>\n";

    private const string OpfEnd = @"</spine>
<tours/>
<guide> <reference type=""search"" title=""Dictionary Search"" onclick= ""index_search()""/> </guide>
</package>
";

    // ReSharper disable once InconsistentNaming
    private const string OpfEntryWithID = @"<idx:entry name=""word"" scriptable=""yes"" id=""{2}"">
<idx:orth value=""{0}"">{3}</idx:orth>
{1}
</idx:entry>
<mbp:pagebreak/>
";

    public override string Description => "写出到文件";
    public override bool EnableAsync => false;

    public override void Do(DictManager manager, TextWriter logger)
    {
        using var cmd = manager.DataBaseConnection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Dictionary";
        var reader = cmd.ExecuteReader();
        var opf = new StreamWriter(Path.Combine(manager.DictionaryDirPath, $"{manager.DictionaryName}.html"));
        opf.AutoFlush = true;
        opf.Write(@"<?xml version=""1.0"" encoding=""utf-8""?>
<html xmlns:idx=""www.mobipocket.com"" xmlns:mbp=""www.mobipocket.com"" xmlns:xlink=""http://www.w3.org/1999/xlink"">");
        if (manager.CssContent is not null)
        {
            opf.Write($"<head><style>\n{manager.CssContent}\n</style></head>\n");
        }

        opf.Write(@"<body>
<mbp:pagebreak/>
<mbp:frameset>
<mbp:slave-frame display=""bottom"" device=""all"" breadth=""auto"" leftmargin=""0"" rightmargin=""0"" bottommargin=""0"" topmargin=""0"">
<div align=""center"" bgcolor=""yellow""/>
<a onclick=""index_search()"">Dictionary Search</a>
</div>
</mbp:slave-frame>
<mbp:pagebreak/>
");

        while (reader.Read())
        {
            // var index = (uint)reader.GetInt32(0);
            var name = reader.GetString(1)!;
            var explanation = reader.GetString(2)!;
            var id = reader.GetString(0)!;
            var infls = reader.GetString(3) ?? "";
            var inflString = infls == "" ? "" : Utils.GetInflString(infls.Split(','));
            opf.Write(OpfEntryWithID,
                name.Replace("@", "at_")
                    .Replace("&", "_and_")
                    .Replace("=", "_eq_"),
                explanation,
                id,
                inflString);
        }

        opf.Write(@"</mbp:frameset></body></html>");
        opf.Close();

        opf = new StreamWriter(File.Create(Path.Combine(manager.DictionaryDirPath, $"{manager.DictionaryName}.opf")));
        opf.AutoFlush = true;

        var ts = DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, 0);
        opf.Write(OpfHead1, Convert.ToInt64(ts.TotalSeconds), manager.DictionaryName);
        opf.Write(OpfHead2);
        opf.Write(OpfLine, manager.DictionaryName);

        opf.Write(OpfMiddle);
        opf.Write(OpfItemRef);

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