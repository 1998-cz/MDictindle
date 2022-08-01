using System.Text;

namespace MDictindle.Step;

public static class Utils
{
    public static int[] GetSubStringIndexes(string str, string substr, int startPos)
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

    /// <summary>
    /// 生成变形词跳转数据
    /// </summary>
    /// <param name="words"></param>
    /// <returns></returns>
    public static string GenerateInfl(IEnumerable<string> words)
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

    public static IEnumerable<List<T>> GetPermutationAndCombination<T>(List<ISet<T>> sets)
    {
        switch (sets.Count)
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
            var res2 = GetPermutationAndCombination(sets.GetRange(1, sets.Count-1));
            foreach (var list in res2)
            {
                list.Insert(0, t);
                res.Add(list);
            }
        }

        return res;
    }

    public static string CreateInflsForAWordOrPhrase(string wordOrPhrase,
        IReadOnlyDictionary<string, ISet<string>> mappings, TextWriter logger)
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

            var res = GetPermutationAndCombination(listInfls);
            var finalPhrases = res.Select(list => string.Join(' ', list)).ToList();

            return GenerateInfl(finalPhrases);
        }
    }
}