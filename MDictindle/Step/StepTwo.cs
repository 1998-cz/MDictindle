using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace MDictindle.Step;

public class AbsStepTwo : AbsStep
{
    public override string Description => "处理链接";
    public override bool EnableAsync => true;

    public override async Task DoAsync(DictManager manager, TextWriter logger)
    {
        await logger.WriteLineAsync($"第二步：共有 {manager.GetCount()} 个条目需要处理，请耐心。除最前面的 2000 条目和最后的几千条目外，每个条目所花费的时间大致相同");
        uint i = 0;

        await using var cmd = manager.DataBaseConnection.CreateCommand();
        cmd.CommandText = "SELECT Id, Explanation FROM Dictionary";
        var reader = await cmd.ExecuteReaderAsync();

        await using var cmdForUpdate = manager.DataBaseConnection.CreateCommand();
        cmdForUpdate.CommandText = "UPDATE Dictionary SET Explanation = @explanation WHERE Id = @id ";

        await using var tran = manager.DataBaseConnection.BeginTransaction();
        const string pattern = "href=\"(bword://(\\S+?))\"";
        var re = new Regex(pattern, RegexOptions.Compiled);
        while (await reader.ReadAsync())
        {
            i++;
            var explanation = reader.GetString(1);
            var collection = re.Matches(explanation);
            
            if (collection.Count == 0)
            {
                continue;
            }
            
            var sourceId = reader.GetString(0);
            var newExplanation = new StringBuilder(explanation);
            foreach (Match match in collection)
            {
                var linked = match.Groups[2].Value;
                if (!linked.Contains('#'))
                {
                    var b = await manager.ContainsId(linked);

                    if (!b)
                    {
                        await logger.WriteLineAsync($"第二步：警告：存在指向 {linked} 的链接，但 {linked} 不存在");
                        continue;
                    }

                    var id = linked;
                    id = Utils.GetId(id);
                    
                    newExplanation.Replace(match.Value,
                        $"href=\"#{id}\"");
                }
                else
                {
                    var split = linked.Split('#');
                    var prefix = split[0];
                    var postfix = split[1];
                    // 优先使用后缀

                    string? id;
                    if (await manager.ContainsId(postfix))
                    {
                        id = postfix;
                    }
                    else if (await manager.ContainsId(prefix))
                    {
                        id = prefix;
                    }
                    else
                    {
                        
                        await logger.WriteLineAsync($"第二步：警告：存在指向 {linked} 的链接，但 {linked} 不存在");
                        continue;
                    }

                    id = Utils.GetId(id);

                    newExplanation.Replace(match.Value, $"href=\"#{id}\"");
                }
            }

            cmdForUpdate.Reset();
            cmdForUpdate.Parameters.Add("@explanation", DbType.String).Value = newExplanation.ToString();
            cmdForUpdate.Parameters.Add("@id", DbType.String).Value = sourceId;
            await cmdForUpdate.PrepareAsync();
            await cmdForUpdate.ExecuteNonQueryAsync();
            if (i % 2000 == 0)
            {
                await logger.WriteLineAsync($"第二步：已经处理了 {i} 个条目");
            }
        }

        await tran.CommitAsync();
    }

    public override void Do(DictManager manager, TextWriter logger)
    {
        throw new NotSupportedException("第二步不支持 Async");
    }
}